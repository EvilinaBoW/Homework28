using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    public class Node
    {
        // Агрегированные значения
        public long Sum { get; set; }
        public long Min { get; set; }
        public long Max { get; set; }

        // Ленивое значение (Lazy Propagation)
        public long LazyValue { get; set; }

        // Указатели на дочерние узлы
        public Node Left { get; set; }
        public Node Right { get; set; }

        public Node()
        {
            // Инициализация нейтральными элементами
            Min = long.MaxValue;
            Max = long.MinValue;
        }

        // Метод для создания копии узла для персистентности (версионирования)
        public Node Clone()
        {
            return new Node
            {
                Sum = Sum,
                Min = Min,
                Max = Max,
                LazyValue = LazyValue,
                Left = Left,
                Right = Right
            };
        }
    }

    // 2. Персистентное Дерево сегментов 
    public class PersistentSegmentTree
    {
        private readonly int _n;
        private readonly List<Node> _roots;

        // Вспомогательный массив для исходных данных
        private readonly long[] _initialArray;

        public PersistentSegmentTree(long[] initialArray)
        {
            _n = initialArray.Length;
            _initialArray = initialArray;
            _roots = new List<Node>();

            // Создание первой (нулевой) версии дерева
            Node rootV0 = Build(0, _n - 1);
            _roots.Add(rootV0);
        }

        // Получить корень определенной версии
        public Node GetRoot(int version)
        {
            if (version < 0 || version >= _roots.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Версия не существует.");
            }
            return _roots[version];
        }

        public int CurrentVersion => _roots.Count - 1;

        // 2.1. Построение дерева 
        private Node Build(int l, int r)
        {
            var node = new Node();

            if (l == r)
            {
                node.Sum = _initialArray[l];
                node.Min = _initialArray[l];
                node.Max = _initialArray[l];
            }
            else
            {
                int mid = l + (r - l) / 2;
                node.Left = Build(l, mid);
                node.Right = Build(mid + 1, r);
                Merge(node);
            }
            return node;
        }

        // Объединение значений из дочерних узлов в родительский
        private void Merge(Node node)
        {
            node.Sum = node.Left.Sum + node.Right.Sum;
            node.Min = Math.Min(node.Left.Min, node.Right.Min);
            node.Max = Math.Max(node.Left.Max, node.Right.Max);
        }

        // --- 2.2. Распространение ленивого значения (O(1)) ---
        private void Push(Node node, int l, int r)
        {
            if (node.LazyValue != 0 && l != r)
            {
                // Для персистентности: клонируем дочерние узлы перед обновлением
                node.Left = node.Left.Clone();
                node.Right = node.Right.Clone();

                int mid = l + (r - l) / 2;
                long val = node.LazyValue;

                // Обновляем левый дочерний узел
                node.Left.LazyValue += val;
                node.Left.Sum += val * (mid - l + 1);
                node.Left.Min += val;
                node.Left.Max += val;

                // Обновляем правый дочерний узел
                node.Right.LazyValue += val;
                node.Right.Sum += val * (r - mid);
                node.Right.Min += val;
                node.Right.Max += val;

                // Сбрасываем ленивое значение родителя
                node.LazyValue = 0;
            }
        }

        // --- 2.3. Обновление диапазона (Range Update) с Lazy Propagation (O(log n)) ---
        // Добавление `delta` к каждому элементу в диапазоне [ql, qr]
        public int UpdateRange(int ql, int qr, long delta)
        {
            // Проверка корректности входных данных
            if (ql < 0 || qr >= _n || ql > qr)
                throw new ArgumentOutOfRangeException($"Неверный диапазон [{ql}, {qr}]");

            // Создаем новую версию, клонируя корень текущей версии
            Node oldRoot = _roots[CurrentVersion];
            Node newRoot = UpdateRange(oldRoot, 0, _n - 1, ql, qr, delta);

            _roots.Add(newRoot);
            return _roots.Count - 1; // Исправлено: возвращаем правильный номер версии
        }

        private Node UpdateRange(Node node, int l, int r, int ql, int qr, long delta)
        {
            // Если узел полностью вне диапазона запроса, возвращаем его без изменений
            if (r < ql || l > qr)
            {
                return node;
            }

            // Клонируем текущий узел для сохранения истории (персистентность)
            Node newNode = node.Clone();

            // Применяем ленивое значение к текущему узлу 
            Push(newNode, l, r);

            // Если узел полностью покрывает диапазон запроса
            if (ql <= l && r <= qr)
            {
                // Применяем обновление лениво к текущему узлу
                newNode.LazyValue += delta;
                newNode.Sum += delta * (r - l + 1);
                newNode.Min += delta;
                newNode.Max += delta;
            }
            else
            {
                // Частичное покрытие: рекурсивно обновляем дочерние узлы
                int mid = l + (r - l) / 2;

                // Заменяем дочерние узлы новыми, рекурсивно созданными узлами
                newNode.Left = UpdateRange(newNode.Left, l, mid, ql, qr, delta);
                newNode.Right = UpdateRange(newNode.Right, mid + 1, r, ql, qr, delta);

                // После обновления дочерних узлов, объединяем их значения
                Merge(newNode);
            }
            return newNode;
        }

        // 2.4. Запрос диапазона (Range Query) 
        public (long Sum, long Min, long Max) QueryRange(int ql, int qr, int version)
        {
            // Проверка корректности входных данных
            if (ql < 0 || qr >= _n || ql > qr)
                throw new ArgumentOutOfRangeException($"Неверный диапазон [{ql}, {qr}]");

            Node root = GetRoot(version);
            return QueryRange(root, 0, _n - 1, ql, qr);
        }

        private (long Sum, long Min, long Max) QueryRange(Node node, int l, int r, int ql, int qr)
        {
            // Если узел полностью вне диапазона запроса
            if (r < ql || l > qr)
            {
                // Возвращаем нейтральные элементы
                return (0, long.MaxValue, long.MinValue);
            }

            // Применяем ленивое значение
            Push(node, l, r);

            // Если узел полностью покрывает диапазон запроса
            if (ql <= l && r <= qr)
            {
                return (node.Sum, node.Min, node.Max);
            }

            // Частичное покрытие - рекурсивно запрашиваем дочерние узлы
            int mid = l + (r - l) / 2;
            var leftResult = QueryRange(node.Left, l, mid, ql, qr);
            var rightResult = QueryRange(node.Right, mid + 1, r, ql, qr);

            // Объединяем результаты
            long sum = leftResult.Sum + rightResult.Sum;
            long min = Math.Min(leftResult.Min, rightResult.Min);
            long max = Math.Max(leftResult.Max, rightResult.Max);

            return (sum, min, max);
        }

        // Дополнительный метод для точечного обновления
        public int UpdatePoint(int index, long value)
        {
            return UpdateRange(index, index, value);
        }
    }

    // 3. Тесты и Демонстрация
    public class SegmentTreeDemo
    {
        public static void RunTests()
        {
            Console.WriteLine(" Демонстрация - Персистентное Дерево Сегментов с Lazy Propagation");

            // Исходный массив
            long[] initialArray = { 1, 2, 3, 4, 5, 6, 7, 8 };
            int n = initialArray.Length;
            var tree = new PersistentSegmentTree(initialArray);

            Console.WriteLine($"\n Исходный массив (Версия 0): [{string.Join(", ", initialArray)}]");

            // Тест 1: Запрос на диапазоне (Версия 0)
            // Запрос [1, 4] (элементы 2, 3, 4, 5). Сумма=14, Мин=2, Макс=5
            int ql1 = 1, qr1 = 4;
            var res1 = tree.QueryRange(ql1, qr1, 0);
            Console.WriteLine($"\n Запрос 1 (Версия 0): Диапазон [{ql1}, {qr1}]");
            Console.WriteLine($"Ожидаемая Сумма: 14, Мин: 2, Макс: 5");
            Console.WriteLine($"Получено: Сумма={res1.Sum}, Мин={res1.Min}, Макс={res1.Max}");

            Console.WriteLine("\n Обновление диапазона с Lazy Propagation и созданием новой версии");

            // Тест 2: Обновление диапазона (Создание Версии 1)
            // Обновление диапазона [3, 6] (элементы 4, 5, 6, 7). Добавить 10.
            // Array[3] = 4+10=14, Array[4]=5+10=15, Array[5]=6+10=16, Array[6]=7+10=17
            int ul2 = 3, ur2 = 6;
            long delta2 = 10;
            int version1 = tree.UpdateRange(ul2, ur2, delta2);
            Console.WriteLine($"Обновление диапазона [{ul2}, {ur2}] на +{delta2}. Создана **Версия {version1}**.");

            // Тест 3: Запрос на Версии 1 
            // Запрос [4, 7] (элементы 15, 16, 17, 8). Сумма=56, Мин=8, Макс=17
            int ql3 = 4, qr3 = 7;
            var res3 = tree.QueryRange(ql3, qr3, version1);
            Console.WriteLine($"\n Запрос 3 (Версия {version1}): Диапазон [{ql3}, {qr3}]");
            Console.WriteLine($"Ожидаемая Сумма: 56, Мин: 8, Макс: 17");
            Console.WriteLine($"Получено: Сумма={res3.Sum}, Мин={res3.Min}, Макс={res3.Max}");

            // Тест 4: Запрос на Версии 0 (Проверка Персистентности) 
            // Запрос [1, 4] (элементы 2, 3, 4, 5). Сумма=14, Мин=2, Макс=5
            int ql4 = 1, qr4 = 4;
            var res4 = tree.QueryRange(ql4, qr4, 0);
            Console.WriteLine($"\n Запрос 4 (Версия 0 - Проверка Персистентности): Диапазон [{ql4}, {qr4}]");
            Console.WriteLine($"Ожидаемая Сумма: 14, Мин: 2, Макс: 5");
            Console.WriteLine($"Получено: Сумма={res4.Sum}, Мин={res4.Min}, Макс={res4.Max}");

            // Тест 5: Создание еще одной версии
            int ul5 = 0, ur5 = 2;
            long delta5 = 5;
            int version2 = tree.UpdateRange(ul5, ur5, delta5);
            Console.WriteLine($"\nОбновление диапазона [{ul5}, {ur5}] на +{delta5}. Создана **Версия {version2}**.");

            // Проверка всех версий
            Console.WriteLine($"\n--- Сравнение всех версий для диапазона [1, 3] ---");
            var ver0 = tree.QueryRange(1, 3, 0);
            var ver1 = tree.QueryRange(1, 3, 1);
            var ver2 = tree.QueryRange(1, 3, 2);

            Console.WriteLine($"Версия 0: Сумма={ver0.Sum}, Мин={ver0.Min}, Макс={ver0.Max}");
            Console.WriteLine($"Версия 1: Сумма={ver1.Sum}, Мин={ver1.Min}, Макс={ver1.Max}");
            Console.WriteLine($"Версия 2: Сумма={ver2.Sum}, Мин={ver2.Min}, Макс={ver2.Max}");
        }

        // 4. Тест Производительности (для n=10^6)
        public static void RunPerformanceTest(int n = 1_000_000)
        {
            Console.WriteLine("\n Тест Производительности");
            Console.WriteLine($"Размер массива n = {n}");

            long[] largeArray = new long[n];
            for (int i = 0; i < n; i++)
            {
                largeArray[i] = i + 1;
            }

            var startTime = DateTime.Now;
            var tree = new PersistentSegmentTree(largeArray);
            var buildTime = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"Время построения (O(n)): {buildTime:F2} мс");

            // 1000 случайных запросов и обновлений (O(log n))
            int numOperations = 1000;
            var random = new Random();
            startTime = DateTime.Now;

            for (int i = 0; i < numOperations; i++)
            {
                int ql = random.Next(0, n / 2);
                int qr = random.Next(n / 2, n);

                if (i % 2 == 0)
                {
                    // Запрос (O(log n))
                    tree.QueryRange(ql, qr, tree.CurrentVersion);
                }
                else
                {
                    // Обновление (O(log n))
                    tree.UpdateRange(ql, qr, random.Next(1, 10));
                }
            }

            var opTime = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"Время {numOperations} операций (O(log n) каждая): {opTime:F2} мс");
            Console.WriteLine($"Среднее время на операцию: {(opTime / numOperations * 1_000_000):F2} нс");
            Console.WriteLine($"Количество созданных версий: {tree.CurrentVersion + 1}");
        }
    }

    public class MainProgram
    {
        public static void Main(string[] args)
        {
            try
            {
                SegmentTreeDemo.RunTests();
                Console.WriteLine("\n------------------------------------------------------------");
                SegmentTreeDemo.RunPerformanceTest(100000); // Уменьшено для быстрого тестирования
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Произошла ошибка: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}