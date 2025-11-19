using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    public class BTreeNode
    {
        // Ключи и дочерние узлы
        public List<int> Keys { get; set; } = new List<int>();
        public List<int> ChildrenPointers { get; set; } = new List<int>(); // Файловые указатели
        public bool IsLeaf { get; set; }
        public int SelfPointer { get; set; } // Уникальный идентификатор узла
        public int KeyCount => Keys.Count;

        // Конструктор для десериализации
        public BTreeNode(bool isLeaf, int selfPointer)
        {
            IsLeaf = isLeaf;
            SelfPointer = selfPointer;
        }

        // Сериализация для записи на диск (оптимизированная версия)
        public byte[] Serialize(int t)
        {
            // Расчет размера блока 
            int maxKeys = 2 * t - 1;
            int estimatedSize = 1 + 4 + 4 + (maxKeys * 4) + 4 + ((maxKeys + 1) * 4);

            using (var ms = new MemoryStream(estimatedSize))
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(IsLeaf);
                writer.Write(SelfPointer);

                // Ключи
                writer.Write(KeyCount);
                for (int i = 0; i < KeyCount; i++)
                    writer.Write(Keys[i]);

                // Дети
                writer.Write(ChildrenPointers.Count);
                for (int i = 0; i < ChildrenPointers.Count; i++)
                    writer.Write(ChildrenPointers[i]);

                return ms.ToArray();
            }
        }

        // Десериализация для чтения с диска
        public static BTreeNode Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                bool isLeaf = reader.ReadBoolean();
                int selfPointer = reader.ReadInt32();
                var node = new BTreeNode(isLeaf, selfPointer);

                // Ключи
                int keyCount = reader.ReadInt32();
                for (int i = 0; i < keyCount; i++)
                    node.Keys.Add(reader.ReadInt32());

                // Дети
                int ptrCount = reader.ReadInt32();
                for (int i = 0; i < ptrCount; i++)
                    node.ChildrenPointers.Add(reader.ReadInt32());

                return node;
            }
        }

        public override string ToString()
        {
            return $"Node[{SelfPointer}]: Keys={{{string.Join(",", Keys)}}}, Children={{{string.Join(",", ChildrenPointers)}}}, IsLeaf={IsLeaf}";
        }
    }

    // 2. Интерфейс дискового Хранилища 
    public interface IDiskStorage
    {
        byte[] ReadBlock(int blockId);
        void WriteBlock(int blockId, byte[] data);
        int AllocateBlock();
        void DeallocateBlock(int blockId);
        int BlockSize { get; }
    }

    // 3. Реализация файлового хранилища 
    public class FileDiskStorage : IDiskStorage
    {
        private readonly string _filePath;
        private readonly int _blockSize;
        private readonly Dictionary<int, byte[]> _cache = new Dictionary<int, byte[]>();
        private int _nextBlockId = 0;

        public FileDiskStorage(string filePath, int blockSize = 4096)
        {
            _filePath = filePath;
            _blockSize = blockSize;
            InitializeStorage();
        }

        public int BlockSize => _blockSize;

        private void InitializeStorage()
        {
            if (File.Exists(_filePath))
            {
                LoadExistingFile();
            }
            else
            {
                // Создаем файл с заголовком
                using (var fs = new FileStream(_filePath, FileMode.Create))
                {
                    // Заголовок размер блока (4 байта) + количество блоков (4 байта)
                    var header = new byte[8];
                    BitConverter.GetBytes(_blockSize).CopyTo(header, 0);
                    BitConverter.GetBytes(0).CopyTo(header, 4); // начальное кол-во блоков
                    fs.Write(header, 0, header.Length);
                }
            }
        }

        private void LoadExistingFile()
        {
            using (var fs = new FileStream(_filePath, FileMode.Open))
            using (var reader = new BinaryReader(fs))
            {
                int storedBlockSize = reader.ReadInt32();
                if (storedBlockSize != _blockSize)
                    throw new InvalidOperationException("Block size mismatch");

                _nextBlockId = reader.ReadInt32();
            }
        }

        private void UpdateHeader()
        {
            using (var fs = new FileStream(_filePath, FileMode.Open))
            {
                fs.Write(BitConverter.GetBytes(_blockSize), 0, 4);
                fs.Write(BitConverter.GetBytes(_nextBlockId), 0, 4);
            }
        }

        public byte[] ReadBlock(int blockId)
        {
            if (_cache.TryGetValue(blockId, out var cachedData))
                return cachedData;

            using (var fs = new FileStream(_filePath, FileMode.Open))
            {
                long position = 8 + (long)blockId * _blockSize;
                if (position >= fs.Length)
                    throw new ArgumentException($"Block {blockId} does not exist");

                fs.Seek(position, SeekOrigin.Begin);
                byte[] data = new byte[_blockSize];
                fs.Read(data, 0, _blockSize);

                // Кэшируем прочитанный блок
                _cache[blockId] = data;
                return data;
            }
        }

        public void WriteBlock(int blockId, byte[] data)
        {
            if (data.Length > _blockSize)
                throw new ArgumentException($"Data exceeds block size: {data.Length} > {_blockSize}");

            // Дополняем данные до размера блока
            byte[] blockData = new byte[_blockSize];
            Array.Copy(data, blockData, data.Length);

            using (var fs = new FileStream(_filePath, FileMode.Open))
            {
                long position = 8 + (long)blockId * _blockSize;
                fs.Seek(position, SeekOrigin.Begin);
                fs.Write(blockData, 0, _blockSize);
            }

            // Обновляем кэш
            _cache[blockId] = data;
        }

        public int AllocateBlock()
        {
            int newBlockId = _nextBlockId++;
            UpdateHeader();
            return newBlockId;
        }

        public void DeallocateBlock(int blockId)
        {
            _cache.Remove(blockId);
        }
    }

    // 4. Класс B-Дерева с полной Функциональностью 
    public class BTree
    {
        private readonly int _t; // Минимальная степень
        private readonly IDiskStorage _disk;
        private readonly LRUCache<int, BTreeNode> _nodeCache;

        public int RootPointer { get; private set; }

        public BTree(int t, IDiskStorage disk, int cacheCapacity = 100)
        {
            if (t < 2) throw new ArgumentException("Minimum degree t must be at least 2");

            _t = t;
            _disk = disk;
            _nodeCache = new LRUCache<int, BTreeNode>(cacheCapacity);

            // Инициализация корня
            if (RootPointer == 0) // Предполагаем что дерево новое
            {
                var root = CreateNode(true);
                RootPointer = root.SelfPointer;
                WriteNode(root);
            }
        }

        // 4.1. Операции с Узлами 
        private BTreeNode CreateNode(bool isLeaf)
        {
            int pointer = _disk.AllocateBlock();
            return new BTreeNode(isLeaf, pointer);
        }

        private BTreeNode ReadNode(int pointer)
        {
            if (_nodeCache.TryGet(pointer, out var cachedNode))
                return cachedNode;

            byte[] data = _disk.ReadBlock(pointer);
            var node = BTreeNode.Deserialize(data);
            _nodeCache.Put(pointer, node);
            return node;
        }

        private void WriteNode(BTreeNode node)
        {
            byte[] data = node.Serialize(_t);
            _disk.WriteBlock(node.SelfPointer, data);
            _nodeCache.Put(node.SelfPointer, node); // Обновляем кэш
        }

        private void DeleteNode(int pointer)
        {
            _nodeCache.Remove(pointer);
            _disk.DeallocateBlock(pointer);
        }

        // 4.2. Поиск
        public bool Search(int key)
        {
            return Search(ReadNode(RootPointer), key);
        }

        private bool Search(BTreeNode node, int key)
        {
            int i = 0;
            while (i < node.KeyCount && key > node.Keys[i])
                i++;

            if (i < node.KeyCount && key == node.Keys[i])
                return true;

            return node.IsLeaf ? false : Search(ReadNode(node.ChildrenPointers[i]), key);
        }

        // 4.3. Вставка
        public void Insert(int key)
        {
            var root = ReadNode(RootPointer);

            if (root.KeyCount == 2 * _t - 1) // Максимальное количество ключей
            {
                // создаем новый корень
                var newRoot = CreateNode(false);
                newRoot.ChildrenPointers.Add(RootPointer);

                RootPointer = newRoot.SelfPointer;
                SplitChild(newRoot, 0, root);
                InsertNonFull(newRoot, key);
            }
            else
            {
                InsertNonFull(root, key);
            }
        }

        private void InsertNonFull(BTreeNode node, int key)
        {
            int i = node.KeyCount - 1;

            if (node.IsLeaf)
            {
                // Вставка в лист
                while (i >= 0 && key < node.Keys[i])
                    i--;

                node.Keys.Insert(i + 1, key);
                WriteNode(node);
            }
            else
            {
                // Находим подходящего потомка
                while (i >= 0 && key < node.Keys[i])
                    i--;
                i++;

                var child = ReadNode(node.ChildrenPointers[i]);
                if (child.KeyCount == 2 * _t - 1)
                {
                    SplitChild(node, i, child);
                    if (key > node.Keys[i])
                        i++;
                }

                InsertNonFull(ReadNode(node.ChildrenPointers[i]), key);
            }
        }

        private void SplitChild(BTreeNode parent, int i, BTreeNode child)
        {
            var sibling = CreateNode(child.IsLeaf);

            // Переносим ключи
            sibling.Keys.AddRange(child.Keys.GetRange(_t, _t - 1));
            child.Keys.RemoveRange(_t - 1, _t); // Удаляем перенесенные ключи и медиану

            // Переносим потомков 
            if (!child.IsLeaf)
            {
                sibling.ChildrenPointers.AddRange(child.ChildrenPointers.GetRange(_t, _t));
                child.ChildrenPointers.RemoveRange(_t, _t);
            }

            // Поднимаем медиану в родителя
            int medianKey = child.Keys[_t - 1];
            child.Keys.RemoveAt(_t - 1);

            parent.Keys.Insert(i, medianKey);
            parent.ChildrenPointers.Insert(i + 1, sibling.SelfPointer);

            WriteNode(child);
            WriteNode(sibling);
            WriteNode(parent);
        }

        // 4.4. Удаление
        public void Delete(int key)
        {
            var root = ReadNode(RootPointer);
            Delete(root, key);

            // Если корень стал пустым (кроме случая, когда он лист)
            if (root.KeyCount == 0 && !root.IsLeaf)
            {
                DeleteNode(root.SelfPointer);
                RootPointer = root.ChildrenPointers[0];
            }
        }

        private void Delete(BTreeNode node, int key)
        {
            int idx = FindKeyIndex(node, key);

            if (idx < node.KeyCount && node.Keys[idx] == key)
            {
                // Ключ найден в этом узле
                if (node.IsLeaf)
                    DeleteFromLeaf(node, idx);
                else
                    DeleteFromInternalNode(node, idx);
            }
            else
            {
                // Ключ не в этом узле
                if (node.IsLeaf)
                    throw new KeyNotFoundException($"Key {key} not found in tree");

                bool isLastChild = (idx == node.KeyCount);
                var child = ReadNode(node.ChildrenPointers[idx]);

                // Гарантируем, что у ребенка хотя бы t ключей
                if (child.KeyCount < _t)
                    FillChild(node, idx);

                // Определяем, какой ребенок теперь содержит ключ
                if (isLastChild && idx > node.KeyCount)
                    Delete(ReadNode(node.ChildrenPointers[idx - 1]), key);
                else
                    Delete(ReadNode(node.ChildrenPointers[idx]), key);
            }
        }

        private int FindKeyIndex(BTreeNode node, int key)
        {
            int idx = 0;
            while (idx < node.KeyCount && key > node.Keys[idx])
                idx++;
            return idx;
        }

        private void DeleteFromLeaf(BTreeNode node, int idx)
        {
            node.Keys.RemoveAt(idx);
            WriteNode(node);
        }

        private void DeleteFromInternalNode(BTreeNode node, int idx)
        {
            int key = node.Keys[idx];
            var leftChild = ReadNode(node.ChildrenPointers[idx]);
            var rightChild = ReadNode(node.ChildrenPointers[idx + 1]);

            if (leftChild.KeyCount >= _t)
            {
                // Заменяем на предшественника
                int predecessor = GetPredecessor(leftChild);
                node.Keys[idx] = predecessor;
                WriteNode(node);
                Delete(leftChild, predecessor);
            }
            else if (rightChild.KeyCount >= _t)
            {
                // Заменяем на последователя
                int successor = GetSuccessor(rightChild);
                node.Keys[idx] = successor;
                WriteNode(node);
                Delete(rightChild, successor);
            }
            else
            {
                // Сливаем детей
                MergeChildren(node, idx);
                Delete(leftChild, key);
            }
        }

        private int GetPredecessor(BTreeNode node)
        {
            while (!node.IsLeaf)
                node = ReadNode(node.ChildrenPointers[node.KeyCount]);
            return node.Keys[node.KeyCount - 1];
        }

        private int GetSuccessor(BTreeNode node)
        {
            while (!node.IsLeaf)
                node = ReadNode(node.ChildrenPointers[0]);
            return node.Keys[0];
        }

        private void FillChild(BTreeNode parent, int idx)
        {
            if (idx != 0)
            {
                var leftSibling = ReadNode(parent.ChildrenPointers[idx - 1]);
                if (leftSibling.KeyCount >= _t)
                {
                    // Забираем ключ у левого брата
                    BorrowFromLeft(parent, idx, leftSibling, ReadNode(parent.ChildrenPointers[idx]));
                    return;
                }
            }

            if (idx != parent.KeyCount)
            {
                var rightSibling = ReadNode(parent.ChildrenPointers[idx + 1]);
                if (rightSibling.KeyCount >= _t)
                {
                    // Забираем ключ у правого брата
                    BorrowFromRight(parent, idx, ReadNode(parent.ChildrenPointers[idx]), rightSibling);
                    return;
                }
            }

            // Сливаем с братом
            if (idx != parent.KeyCount)
                MergeChildren(parent, idx);
            else
                MergeChildren(parent, idx - 1);
        }

        private void BorrowFromLeft(BTreeNode parent, int idx, BTreeNode leftSibling, BTreeNode child)
        {
            // Перемещаем ключ от родителя к ребенку
            child.Keys.Insert(0, parent.Keys[idx - 1]);

            // Перемещаем ключ от левого брата к родителю
            parent.Keys[idx - 1] = leftSibling.Keys[leftSibling.KeyCount - 1];
            leftSibling.Keys.RemoveAt(leftSibling.KeyCount - 1);

            // Перемещаем указатель, если не листья
            if (!leftSibling.IsLeaf)
            {
                child.ChildrenPointers.Insert(0, leftSibling.ChildrenPointers[leftSibling.ChildrenPointers.Count - 1]);
                leftSibling.ChildrenPointers.RemoveAt(leftSibling.ChildrenPointers.Count - 1);
            }

            WriteNode(leftSibling);
            WriteNode(child);
            WriteNode(parent);
        }

        private void BorrowFromRight(BTreeNode parent, int idx, BTreeNode child, BTreeNode rightSibling)
        {
            // Перемещаем ключ от родителя к ребенку
            child.Keys.Add(parent.Keys[idx]);

            // Перемещаем ключ от правого брата к родителю
            parent.Keys[idx] = rightSibling.Keys[0];
            rightSibling.Keys.RemoveAt(0);

            // Перемещаем указатель, если не листья
            if (!rightSibling.IsLeaf)
            {
                child.ChildrenPointers.Add(rightSibling.ChildrenPointers[0]);
                rightSibling.ChildrenPointers.RemoveAt(0);
            }

            WriteNode(rightSibling);
            WriteNode(child);
            WriteNode(parent);
        }

        private void MergeChildren(BTreeNode parent, int idx)
        {
            var leftChild = ReadNode(parent.ChildrenPointers[idx]);
            var rightChild = ReadNode(parent.ChildrenPointers[idx + 1]);

            // Перемещаем ключ от родителя к левому ребенку
            leftChild.Keys.Add(parent.Keys[idx]);

            // Перемещаем ключи от правого ребенка
            leftChild.Keys.AddRange(rightChild.Keys);

            // Перемещаем указатели
            if (!leftChild.IsLeaf)
                leftChild.ChildrenPointers.AddRange(rightChild.ChildrenPointers);

            // Удаляем ключ и указатель из родителя
            parent.Keys.RemoveAt(idx);
            parent.ChildrenPointers.RemoveAt(idx + 1);

            WriteNode(leftChild);
            WriteNode(parent);
            DeleteNode(rightChild.SelfPointer);
        }

        // 4.5. Вспомогательные методы 
        public void PrintTree()
        {
            Console.WriteLine("B-Tree Structure:");
            PrintTree(ReadNode(RootPointer), 0);
        }

        private void PrintTree(BTreeNode node, int depth)
        {
            string indent = new string(' ', depth * 4);
            Console.WriteLine($"{indent}{node}");

            if (!node.IsLeaf)
            {
                foreach (var childPointer in node.ChildrenPointers)
                {
                    PrintTree(ReadNode(childPointer), depth + 1);
                }
            }
        }

        public int GetHeight()
        {
            return GetHeight(ReadNode(RootPointer));
        }

        private int GetHeight(BTreeNode node)
        {
            if (node.IsLeaf) return 1;
            return 1 + GetHeight(ReadNode(node.ChildrenPointers[0]));
        }
    }

    // 5. LRU Кэш для Оптимизации
    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cacheMap;
        private readonly LinkedList<CacheItem> _lruList;

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _cacheMap = new Dictionary<TKey, LinkedListNode<CacheItem>>();
            _lruList = new LinkedList<CacheItem>();
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return true;
            }

            value = default(TValue);
            return false;
        }

        public void Put(TKey key, TValue value)
        {
            if (_cacheMap.TryGetValue(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
            }
            else if (_cacheMap.Count >= _capacity)
            {
                RemoveLeastRecentlyUsed();
            }

            var newNode = new LinkedListNode<CacheItem>(new CacheItem(key, value));
            _lruList.AddFirst(newNode);
            _cacheMap[key] = newNode;
        }

        public void Remove(TKey key)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _cacheMap.Remove(key);
            }
        }

        private void RemoveLeastRecentlyUsed()
        {
            var lastNode = _lruList.Last;
            if (lastNode != null)
            {
                _cacheMap.Remove(lastNode.Value.Key);
                _lruList.RemoveLast();
            }
        }

        private class CacheItem
        {
            public TKey Key { get; }
            public TValue Value { get; }

            public CacheItem(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
    }

    // 6. Демонстрация и Тестирование
    public class BTreeDemo
    {
        public static void RunTests()
        {
            Console.WriteLine("B-Дерево (Полная Реализация)");

            // Тест 1: В памяти (эмуляция диска)
            Console.WriteLine("\nТест 1: Эмуляция в памяти");
            var memoryDisk = new FileDiskStorage("test_btree.dat", 4096);
            var tree = new BTree(t: 3, memoryDisk, cacheCapacity: 50);

            int[] data = { 10, 20, 30, 40, 50, 60, 70, 80, 5, 15, 25, 35, 45, 55, 65, 75, 85 };

            Console.Write("Вставка ключей: ");
            foreach (var key in data)
            {
                tree.Insert(key);
                Console.Write($"{key} ");
            }

            Console.WriteLine("\n\nСтруктура дерева:");
            tree.PrintTree();

            Console.WriteLine($"\nВысота дерева: {tree.GetHeight()}");

            Console.WriteLine("\nТесты поиска:");
            TestSearch(tree, 40, true);
            TestSearch(tree, 25, true);
            TestSearch(tree, 99, false);
            TestSearch(tree, 1, false);

            Console.WriteLine("\nТесты удаления:");
            TestDelete(tree, 25, "существующий ключ");
            TestDelete(tree, 45, "существующий ключ");
            TestDelete(tree, 99, "несуществующий ключ");

            Console.WriteLine("\nСтруктура после удалений:");
            tree.PrintTree();

            // Тест 2 - Производительность
            Console.WriteLine("\n### Тест 2: Производительность с большими данными");
            PerformanceTest();
        }

        private static void TestSearch(BTree tree, int key, bool expected)
        {
            bool result = tree.Search(key);
            Console.WriteLine($"Поиск {key}: {result} (ожидается: {expected}) - {(result == expected ? "✓" : "✗")}");
        }

        private static void TestDelete(BTree tree, int key, string description)
        {
            try
            {
                tree.Delete(key);
                bool exists = tree.Search(key);
                Console.WriteLine($"Удаление {key} ({description}): {(exists ? "НЕ УДАЛЕН" : "успешно")} - {(exists ? "✗" : "✓")}");
            }
            catch (KeyNotFoundException)
            {
                Console.WriteLine($"Удаление {key} ({description}): ключ не найден (ожидаемо) - ✓");
            }
        }

        private static void PerformanceTest()
        {
            var disk = new FileDiskStorage("performance_test.dat", 4096);
            var largeTree = new BTree(t: 50, disk, cacheCapacity: 200); // Большой порядок для теста производительности

            int testSize = 1000;
            var random = new Random(42);

            Console.WriteLine($"Вставка {testSize} случайных ключей...");
            var insertTime = MeasureTime(() =>
            {
                for (int i = 0; i < testSize; i++)
                {
                    largeTree.Insert(random.Next(1, 10000));
                }
            });

            Console.WriteLine($"Поиск {testSize} случайных ключей...");
            var searchTime = MeasureTime(() =>
            {
                for (int i = 0; i < testSize; i++)
                {
                    largeTree.Search(random.Next(1, 10000));
                }
            });

            Console.WriteLine($"Производительность:");
            Console.WriteLine($"- Вставка: {insertTime.TotalMilliseconds:F2} мс");
            Console.WriteLine($"- Поиск: {searchTime.TotalMilliseconds:F2} мс");
            Console.WriteLine($"- Высота дерева: {largeTree.GetHeight()}");
        }

        private static TimeSpan MeasureTime(Action action)
        {
            var start = DateTime.Now;
            action();
            return DateTime.Now - start;
        }
    }

    // Запуск демонстрации
    class Program
    {
        static void Main(string[] args)
        {
            BTreeDemo.RunTests();

            Console.WriteLine("\nДемонстрация завершена!");
            Console.WriteLine("Для выхода нажмите любую клавишу...");
            Console.ReadKey();
        }
    }
}
