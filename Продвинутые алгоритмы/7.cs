using System;
using System.Collections.Generic;

namespace ConsoleApp14
{
    // Упрощенная версия Fibonacci Heap (для демонстрации принципов)
    public class FibonacciHeapItem<T> where T : IEquatable<T>
    {
        public T Value { get; set; }
        public long Priority { get; set; }
        public FibonacciHeapItem<T> Parent { get; set; }
        public FibonacciHeapItem<T> Child { get; set; }
        public FibonacciHeapItem<T> Left { get; set; }
        public FibonacciHeapItem<T> Right { get; set; }
        public int Degree { get; set; }
        public bool Marked { get; set; }
        
        public FibonacciHeapItem(T value, long priority)
        {
            Value = value;
            Priority = priority;
            Left = this;
            Right = this;
        }
    }

    // Fibonacci Heap - приближенная к O(1) для Insert и DecreaseKey
    public class FibonacciMinHeap<T> where T : IEquatable<T>
    {
        private FibonacciHeapItem<T> _minNode;
        private int _count;
        private readonly Dictionary<T, FibonacciHeapItem<T>> _indexMap = new Dictionary<T, FibonacciHeapItem<T>>();

        public bool IsEmpty => _minNode == null;
        public int Count => _count;

        // Метод Contains для проверки наличия элемента
        public bool Contains(T value)
        {
            return _indexMap.ContainsKey(value);
        }

        // O(1) - вставка
        public void Insert(T value, long priority)
        {
            var newItem = new FibonacciHeapItem<T>(value, priority);
            _indexMap[value] = newItem;

            if (_minNode == null)
            {
                _minNode = newItem;
            }
            else
            {
                // Добавляем в корневой список
                InsertIntoRootList(newItem);
                
                // Обновляем минимум если нужно
                if (newItem.Priority < _minNode.Priority)
                {
                    _minNode = newItem;
                }
            }
            _count++;
        }

        // O(log n) - извлечение минимума
        public T ExtractMin()
        {
            if (_minNode == null)
                throw new InvalidOperationException("Heap is empty");

            var minItem = _minNode;
            _indexMap.Remove(minItem.Value);

            // Добавляем детей minItem в корневой список
            if (minItem.Child != null)
            {
                var child = minItem.Child;
                do
                {
                    var nextChild = child.Right;
                    child.Parent = null;
                    InsertIntoRootList(child);
                    child = nextChild;
                } while (child != minItem.Child);
            }

            // Удаляем minItem из корневого списка
            if (minItem.Right == minItem)
            {
                _minNode = null;
            }
            else
            {
                _minNode = minItem.Right;
                RemoveFromRootList(minItem);
                Consolidate();
            }

            _count--;
            return minItem.Value;
        }

        // Получение минимального элемента без извлечения
        public T PeekMin()
        {
            if (_minNode == null)
                throw new InvalidOperationException("Heap is empty");
            return _minNode.Value;
        }

        // O(1) - уменьшение приоритета (амортизированно)
        public void DecreasePriority(T value, long newPriority)
        {
            if (!_indexMap.TryGetValue(value, out var item))
                throw new ArgumentException("Item not found");

            if (newPriority > item.Priority)
                throw new InvalidOperationException("New priority is greater than current");

            item.Priority = newPriority;
            var parent = item.Parent;

            if (parent != null && item.Priority < parent.Priority)
            {
                Cut(item, parent);
                CascadingCut(parent);
            }

            if (item.Priority < _minNode.Priority)
            {
                _minNode = item;
            }
        }

        // O(log n) - удаление
        public void Remove(T value)
        {
            if (!Contains(value))
                return;

            DecreasePriority(value, long.MinValue);
            ExtractMin();
        }

        // Вспомогательные методы
        private void InsertIntoRootList(FibonacciHeapItem<T> node)
        {
            if (_minNode == null) return;

            node.Left = _minNode.Left;
            node.Right = _minNode;
            _minNode.Left.Right = node;
            _minNode.Left = node;
        }

        private void RemoveFromRootList(FibonacciHeapItem<T> node)
        {
            node.Left.Right = node.Right;
            node.Right.Left = node.Left;
            node.Left = node;
            node.Right = node;
        }

        private void Consolidate()
        {
            if (_minNode == null) return;

            var degreeTable = new FibonacciHeapItem<T>[64]; // Макс степень ~2^64
            var nodes = new List<FibonacciHeapItem<T>>();
            
            // Собираем все корневые узлы
            var current = _minNode;
            do
            {
                nodes.Add(current);
                current = current.Right;
            } while (current != _minNode);

            foreach (var node in nodes)
            {
                var x = node;
                var degree = x.Degree;
                
                while (degreeTable[degree] != null)
                {
                    var y = degreeTable[degree];
                    if (x.Priority > y.Priority)
                    {
                        (x, y) = (y, x);
                    }
                    Link(y, x);
                    degreeTable[degree] = null;
                    degree++;
                }
                degreeTable[degree] = x;
            }

            // Восстанавливаем минимум и корневой список
            _minNode = null;
            for (int i = 0; i < degreeTable.Length; i++)
            {
                if (degreeTable[i] != null)
                {
                    if (_minNode == null)
                    {
                        _minNode = degreeTable[i];
                        _minNode.Left = _minNode;
                        _minNode.Right = _minNode;
                    }
                    else
                    {
                        InsertIntoRootList(degreeTable[i]);
                        if (degreeTable[i].Priority < _minNode.Priority)
                        {
                            _minNode = degreeTable[i];
                        }
                    }
                }
            }
        }

        private void Link(FibonacciHeapItem<T> child, FibonacciHeapItem<T> parent)
        {
            RemoveFromRootList(child);
            child.Parent = parent;
            
            if (parent.Child == null)
            {
                parent.Child = child;
                child.Left = child;
                child.Right = child;
            }
            else
            {
                child.Left = parent.Child.Left;
                child.Right = parent.Child;
                parent.Child.Left.Right = child;
                parent.Child.Left = child;
            }
            
            parent.Degree++;
            child.Marked = false;
        }

        private void Cut(FibonacciHeapItem<T> child, FibonacciHeapItem<T> parent)
        {
            // Удаляем child из списка детей parent
            if (child.Right == child)
            {
                parent.Child = null;
            }
            else
            {
                child.Left.Right = child.Right;
                child.Right.Left = child.Left;
                if (parent.Child == child)
                {
                    parent.Child = child.Right;
                }
            }
            
            parent.Degree--;
            InsertIntoRootList(child);
            child.Parent = null;
            child.Marked = false;
        }

        private void CascadingCut(FibonacciHeapItem<T> node)
        {
            var parent = node.Parent;
            if (parent != null)
            {
                if (!node.Marked)
                {
                    node.Marked = true;
                }
                else
                {
                    Cut(node, parent);
                    CascadingCut(parent);
                }
            }
        }

        // Очистка кучи
        public void Clear()
        {
            _minNode = null;
            _count = 0;
            _indexMap.Clear();
        }
    }

    // Демонстрация использования в алгоритме Дейкстры
    public class Graph
    {
        private readonly Dictionary<int, List<(int vertex, long weight)>> _adjacencyList = new();

        public void AddEdge(int from, int to, long weight)
        {
            if (!_adjacencyList.ContainsKey(from))
                _adjacencyList[from] = new List<(int, long)>();
            _adjacencyList[from].Add((to, weight));
        }

        public void AddVertex(int vertex)
        {
            if (!_adjacencyList.ContainsKey(vertex))
                _adjacencyList[vertex] = new List<(int, long)>();
        }

        public Dictionary<int, long> Dijkstra(int startVertex)
        {
            var distances = new Dictionary<int, long>();
            var heap = new FibonacciMinHeap<int>();

            // Инициализация
            foreach (var vertex in _adjacencyList.Keys)
            {
                distances[vertex] = long.MaxValue;
            }
            distances[startVertex] = 0;
            heap.Insert(startVertex, 0);

            while (!heap.IsEmpty)
            {
                var currentVertex = heap.ExtractMin();
                var currentDistance = distances[currentVertex];

                if (!_adjacencyList.ContainsKey(currentVertex)) continue;

                foreach (var (neighbor, weight) in _adjacencyList[currentVertex])
                {
                    var newDistance = currentDistance + weight;
                    
                    if (newDistance < distances[neighbor])
                    {
                        distances[neighbor] = newDistance;
                        
                        if (heap.Contains(neighbor))
                        {
                            heap.DecreasePriority(neighbor, newDistance);
                        }
                        else
                        {
                            heap.Insert(neighbor, newDistance);
                        }
                    }
                }
            }

            return distances;
        }
    }

    // Класс для тестирования производительности
    public class PerformanceTester
    {
        public static void TestFibonacciHeapPerformance()
        {
            var heap = new FibonacciMinHeap<int>();
            var random = new Random();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Тест вставки
            Console.WriteLine("Тестирование производительности вставки...");
            watch.Restart();
            for (int i = 0; i < 10000; i++)
            {
                heap.Insert(i, random.Next(1, 100000));
            }
            watch.Stop();
            Console.WriteLine($"Вставка 10000 элементов: {watch.ElapsedMilliseconds} мс");

            // Тест уменьшения приоритета
            Console.WriteLine("Тестирование уменьшения приоритета...");
            watch.Restart();
            for (int i = 0; i < 1000; i++)
            {
                heap.DecreasePriority(i, 1);
            }
            watch.Stop();
            Console.WriteLine($"Уменьшение приоритета 1000 элементов: {watch.ElapsedMilliseconds} мс");

            // Тест извлечения
            Console.WriteLine("Тестирование извлечения минимума...");
            watch.Restart();
            while (!heap.IsEmpty)
            {
                heap.ExtractMin();
            }
            watch.Stop();
            Console.WriteLine($"Извлечение всех элементов: {watch.ElapsedMilliseconds} мс");
        }
    }

    public class PriorityQueueDemo
    {
        public static void RunTests()
        {
            Console.WriteLine("=== Тест 1.7: Fibonacci Heap (O(1) DecreaseKey) ===");

            // Базовые тесты
            TestBasicOperations();
            Console.WriteLine();

            // Тест алгоритма Дейкстры
            TestDijkstra();
            Console.WriteLine();

            // Тест производительности
            PerformanceTester.TestFibonacciHeapPerformance();
        }

        private static void TestBasicOperations()
        {
            Console.WriteLine("Базовые операции:");
            var heap = new FibonacciMinHeap<string>();

            // Вставка за O(1)
            heap.Insert("A", 10);
            heap.Insert("B", 50);
            heap.Insert("C", 20);
            heap.Insert("D", 5);

            Console.WriteLine($"Минимальный элемент: {heap.PeekMin()} (Ожидаемо: D)");

            // Извлечение минимума за O(log n)
            Console.WriteLine($"Извлечение минимума: {heap.ExtractMin()} (Ожидаемо: D)");

            // DecreaseKey за O(1) амортизированно
            heap.DecreasePriority("B", 8);
            Console.WriteLine("Приоритет B уменьшен с 50 до 8.");

            // Проверка наличия элементов
            Console.WriteLine($"Куча содержит 'A': {heap.Contains("A")}");
            Console.WriteLine($"Куча содержит 'D': {heap.Contains("D")}");

            // Извлечение оставшихся элементов
            Console.WriteLine($"Извлечение минимума: {heap.ExtractMin()} (Ожидаемо: B)");
            Console.WriteLine($"Извлечение минимума: {heap.ExtractMin()} (Ожидаемо: A)");
            Console.WriteLine($"Извлечение минимума: {heap.ExtractMin()} (Ожидаемо: C)");
            Console.WriteLine($"Куча пуста: {heap.IsEmpty}");
        }

        private static void TestDijkstra()
        {
            Console.WriteLine("Тест алгоритма Дейкстры:");
            
            var graph = new Graph();
            
            // Создаем простой граф
            graph.AddEdge(0, 1, 4);
            graph.AddEdge(0, 2, 1);
            graph.AddEdge(2, 1, 2);
            graph.AddEdge(2, 3, 5);
            graph.AddEdge(1, 3, 1);
            
            // Добавляем изолированные вершины
            graph.AddVertex(4);

            var distances = graph.Dijkstra(0);

            Console.WriteLine("Кратчайшие расстояния от вершины 0:");
            foreach (var (vertex, distance) in distances)
            {
                Console.WriteLine($"Вершина {vertex}: {distance}");
            }
        }
    }

    // Главный класс программы
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                PriorityQueueDemo.RunTests();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.WriteLine($"Стек вызовов: {ex.StackTrace}");
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}
