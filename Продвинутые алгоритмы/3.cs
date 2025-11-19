using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    public class Edge
    {
        public int Target { get; }
        public long Weight { get; }
        public int Id { get; }

        public Edge(int target, long weight, int id)
        {
            Target = target;
            Weight = weight;
            Id = id;
        }
    }

    public class Vertex
    {
        public int Index { get; }
        public long Distance { get; }

        public Vertex(int index, long distance)
        {
            Index = index;
            Distance = distance;
        }
    }

    public class PriorityQueue
    {
        private readonly List<Vertex> heap = new List<Vertex>();

        public bool IsEmpty => heap.Count == 0;

        public void Enqueue(Vertex vertex)
        {
            heap.Add(vertex);
            SiftUp(heap.Count - 1);
        }

        public Vertex Dequeue()
        {
            if (IsEmpty) throw new InvalidOperationException("Queue is empty.");

            var root = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);

            if (heap.Count > 0)
            {
                SiftDown(0);
            }

            return root;
        }

        private void SiftUp(int index)
        {
            int parent = (index - 1) / 2;
            while (index > 0 && heap[index].Distance < heap[parent].Distance)
            {
                (heap[index], heap[parent]) = (heap[parent], heap[index]);
                index = parent;
                parent = (index - 1) / 2;
            }
        }

        private void SiftDown(int index)
        {
            int leftChild = 2 * index + 1;
            int rightChild = 2 * index + 2;
            int smallest = index;

            if (leftChild < heap.Count && heap[leftChild].Distance < heap[smallest].Distance)
            {
                smallest = leftChild;
            }
            if (rightChild < heap.Count && heap[rightChild].Distance < heap[smallest].Distance)
            {
                smallest = rightChild;
            }

            if (smallest != index)
            {
                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                SiftDown(smallest);
            }
        }
    }

    public class DijkstraSolver
    {
        private readonly List<Edge>[] _adj;
        private readonly int _v;

        // Делаем константу публичной
        public const long Infinity = long.MaxValue / 2;

        public DijkstraSolver(int v)
        {
            _v = v;
            _adj = new List<Edge>[v];
            for (int i = 0; i < v; i++)
            {
                _adj[i] = new List<Edge>();
            }
        }

        public void AddEdge(int u, int v, long weight, bool directed = false)
        {
            _adj[u].Add(new Edge(v, weight, _adj[u].Count));
            if (!directed)
            {
                _adj[v].Add(new Edge(u, weight, _adj[v].Count));
            }
        }

        public DijkstraResult FindAllShortestPaths(int startNode, int endNode)
        {
            long[] dist = new long[_v];
            long[] pathsCount = new long[_v];
            List<int>[] prev = new List<int>[_v];

            for (int i = 0; i < _v; i++)
            {
                dist[i] = Infinity;
                pathsCount[i] = 0;
                prev[i] = new List<int>();
            }

            dist[startNode] = 0;
            pathsCount[startNode] = 1;

            var pq = new PriorityQueue();
            pq.Enqueue(new Vertex(startNode, 0));

            if (startNode < 0 || startNode >= _v || endNode < 0 || endNode >= _v)
            {
                throw new ArgumentOutOfRangeException("Начальная или конечная вершина вне диапазона.");
            }

            while (!pq.IsEmpty)
            {
                var current = pq.Dequeue();
                int u = current.Index;
                long d = current.Distance;

                if (d > dist[u]) continue;

                foreach (var edge in _adj[u])
                {
                    int v = edge.Target;
                    long w = edge.Weight;

                    if (w < 0)
                    {
                        throw new InvalidOperationException("Обнаружены отрицательные веса! Алгоритм Дейкстры не применим.");
                    }

                    if (dist[u] + w < dist[v])
                    {
                        dist[v] = dist[u] + w;
                        pathsCount[v] = pathsCount[u];
                        prev[v].Clear();
                        prev[v].Add(u);
                        pq.Enqueue(new Vertex(v, dist[v]));
                    }
                    else if (dist[u] + w == dist[v])
                    {
                        pathsCount[v] += pathsCount[u];
                        prev[v].Add(u);
                    }
                }
            }

            return new DijkstraResult
            {
                StartNode = startNode,
                EndNode = endNode,
                MinDistance = dist[endNode],
                PathsCount = pathsCount[endNode],
                Predecessors = prev,
                Distances = dist
            };
        }
    }

    public class DijkstraResult
    {
        public int StartNode { get; set; }
        public int EndNode { get; set; }
        public long MinDistance { get; set; }
        public long PathsCount { get; set; }
        public List<int>[] Predecessors { get; set; }
        public long[] Distances { get; set; }

        // Используем публичную константу через экземпляр
        public bool IsUnreachable => MinDistance >= DijkstraSolver.Infinity - 1000; // Небольшой запас

        public List<List<int>> GetAllShortestPaths()
        {
            var allPaths = new List<List<int>>();

            // Используем свойство IsUnreachable вместо прямой проверки 
            if (IsUnreachable)
            {
                return allPaths;
            }

            TraversePaths(EndNode, new List<int> { EndNode }, allPaths);

            foreach (var path in allPaths)
            {
                path.Reverse();
            }

            return allPaths;
        }

        private void TraversePaths(int u, List<int> currentPath, List<List<int>> allPaths)
        {
            if (u == StartNode)
            {
                allPaths.Add(new List<int>(currentPath));
                return;
            }

            foreach (int v in Predecessors[u])
            {
                currentPath.Add(v);
                TraversePaths(v, currentPath, allPaths);
                currentPath.RemoveAt(currentPath.Count - 1);
            }
        }
    }

    public class DijkstraDemo
    {
        public static void RunTests()
        {
            Console.WriteLine("Алгоритм Дейкстры с Восстановлением Всех Кратчайших Путей");

            // Тест 1 -  Граф с двумя кратчайшими путями
            TestMultiplePaths();

            // Тест 2 - Несвязный граф
            TestDisconnectedGraph();

            // Тест 3 -  Большой граф (демонстрация производительности)
            TestLargeGraph();
        }

        private static void TestMultiplePaths()
        {
            Console.WriteLine("\n Тест 1 - Граф с несколькими кратчайшими путями");

            var solver = new DijkstraSolver(5);

            // Создаем два кратчайших пути длины 3
            solver.AddEdge(0, 1, 1);
            solver.AddEdge(0, 2, 2);
            solver.AddEdge(1, 4, 2);
            solver.AddEdge(2, 4, 1);

            int start = 0;
            int end = 4;

            var result = solver.FindAllShortestPaths(start, end);
            var allPaths = result.GetAllShortestPaths();

            Console.WriteLine($"Кратчайшее расстояние: {result.MinDistance}");
            Console.WriteLine($"Количество кратчайших путей: {result.PathsCount}");
            Console.WriteLine("Восстановленные пути:");
            for (int i = 0; i < allPaths.Count; i++)
            {
                Console.WriteLine($"  Путь {i + 1}: {string.Join(" -> ", allPaths[i])}");
            }
        }

        private static void TestDisconnectedGraph()
        {
            Console.WriteLine("\nТест 2: Несвязный граф");

            var solver = new DijkstraSolver(4);

            // Граф: 0-1 и 2-3 (две отдельные компоненты)
            solver.AddEdge(0, 1, 1);
            solver.AddEdge(2, 3, 1);

            var result = solver.FindAllShortestPaths(0, 3);
            var allPaths = result.GetAllShortestPaths();

            // используем свойство IsUnreachable
            if (result.IsUnreachable)
            {
                Console.WriteLine(" Верно обнаружена несвязная компонента");
                Console.WriteLine($"Путь из 0 в 3 не существует");
            }
            else
            {
                Console.WriteLine("Пути найдены:");
                foreach (var path in allPaths)
                {
                    Console.WriteLine($"  {string.Join(" -> ", path)}");
                }
            }
        }

        private static void TestLargeGraph()
        {
            Console.WriteLine("\n Тест 3 - Производительность на большом графе (1000 вершин)");

            int n = 1000;
            var solver = new DijkstraSolver(n);
            var random = new Random();

            // Создаем связный граф
            for (int i = 0; i < n - 1; i++)
            {
                solver.AddEdge(i, i + 1, random.Next(1, 10));
            }

            // Добавляем случайные ребра
            for (int i = 0; i < n * 2; i++)
            {
                int u = random.Next(n);
                int v = random.Next(n);
                if (u != v)
                {
                    solver.AddEdge(u, v, random.Next(1, 20));
                }
            }

            var result = solver.FindAllShortestPaths(0, n - 1);

            Console.WriteLine($"Граф из {n} вершин обработан успешно");
            Console.WriteLine($"Кратчайшее расстояние: {result.MinDistance}");
            Console.WriteLine($"Количество путей: {result.PathsCount}");
        }
    }

    public class MainApp
    {
        public static void Main(string[] args)
        {
            try
            {
                DijkstraDemo.RunTests();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка: {ex.Message}");
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}
