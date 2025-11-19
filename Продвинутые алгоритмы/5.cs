using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    public enum UnionStrategy { ByRank, BySize }

    public class DSU
    {
        private readonly int[] _parent;        // Массив родительских элементов
        private readonly int[] _rankOrSize;    // Универсальный массив: ранг ИЛИ размер
        private int _components;               // Количество компонент связности
        private readonly UnionStrategy _strategy; // Стратегия объединения

        // Структура для записи изменений (для поддержки отката)
        private struct Change
        {
            public int Element;        // Элемент, который был изменен
            public int OldValue;       // Старое значение 
            public bool IsParentChange;// true: parent, false: rank/size
        }

        // Стек для истории всех изменений в массивах parent и rank/size
        private readonly Stack<Change> _history = new Stack<Change>();

        public DSU(int n, UnionStrategy strategy = UnionStrategy.ByRank)
        {
            if (n <= 0)
                throw new ArgumentException("Размер должен быть положительным числом", nameof(n));

            _parent = new int[n];
            _rankOrSize = new int[n];
            _components = n;
            _strategy = strategy;

            // Инициализация: каждый элемент является родителем самого себя
            for (int i = 0; i < n; i++)
            {
                _parent[i] = i;
                _rankOrSize[i] = (strategy == UnionStrategy.BySize) ? 1 : 0; // Для size инициализируем 1
            }
        }

        // Количество компонент связности
        public int ComponentCount => _components;

        // Получить размер компоненты для элемента
        public int GetComponentSize(int i)
        {
            ValidateIndex(i);
            if (_strategy != UnionStrategy.BySize)
                throw new InvalidOperationException("Метод GetComponentSize доступен только для стратегии BySize");

            return _rankOrSize[Find(i)];
        }

        // Получить ранг компоненты для элемента 
        public int GetComponentRank(int i)
        {
            ValidateIndex(i);
            if (_strategy != UnionStrategy.ByRank)
                throw new InvalidOperationException("Метод GetComponentRank доступен только для стратегии ByRank");

            return _rankOrSize[Find(i)];
        }

        // Проверить, связаны ли два элемента
        public bool Connected(int i, int j)
        {
            ValidateIndex(i);
            ValidateIndex(j);
            return Find(i) == Find(j);
        }

        // Операция Find (Найти) с сжатием Пути 
        public int Find(int i)
        {
            ValidateIndex(i);

            // Не добавляем сжатие пути в историю, так как это не меняет
            // состояние множеств (только оптимизирует структуру)
            if (_parent[i] == i)
            {
                return i;
            }

            // Рекурсивный вызов Find и неявное сжатие пути
            return _parent[i] = Find(_parent[i]);
        }

        // Find для отката 
        private int FindNoCompress(int i)
        {
            ValidateIndex(i);

            int root = i;
            while (_parent[root] != root)
            {
                root = _parent[root];
            }
            return root;
        }

        // Универсальная операция Union 
        public bool Union(int i, int j)
        {
            ValidateIndex(i);
            ValidateIndex(j);

            return _strategy == UnionStrategy.ByRank
                ? UnionByRank(i, j)
                : UnionBySize(i, j);
        }

        // Амортизированная сложность
        private bool UnionByRank(int i, int j)
        {
            int rootI = Find(i);
            int rootJ = Find(j);

            if (rootI != rootJ)
            {
                // Отмечаем начало объединения
                _history.Push(new Change { Element = -1 });

                // Объединение по рангу
                if (_rankOrSize[rootI] < _rankOrSize[rootJ])
                {
                    // Сохраняем изменение: parent[rootI] = rootJ
                    SaveChange(rootI, _parent[rootI], true);
                    _parent[rootI] = rootJ;
                }
                else if (_rankOrSize[rootI] > _rankOrSize[rootJ])
                {
                    // Сохраняем изменение: parent[rootJ] = rootI
                    SaveChange(rootJ, _parent[rootJ], true);
                    _parent[rootJ] = rootI;
                }
                else
                {
                    // Ранги равны: объединяем и увеличиваем ранг
                    // Сохраняем изменение: parent[rootJ] = rootI
                    SaveChange(rootJ, _parent[rootJ], true);
                    _parent[rootJ] = rootI;

                    // Сохраняем изменение: rank[rootI]++
                    SaveChange(rootI, _rankOrSize[rootI], false);
                    _rankOrSize[rootI]++;
                }

                _components--;
                return true;
            }
            return false; // Уже в одном множестве
        }

        // Амортизированная сложность
        private bool UnionBySize(int i, int j)
        {
            int rootI = Find(i);
            int rootJ = Find(j);

            if (rootI != rootJ)
            {
                // Отмечаем начало объединения
                _history.Push(new Change { Element = -1 });

                // Объединение по размеру: меньшее дерево присоединяем к большему
                if (_rankOrSize[rootI] < _rankOrSize[rootJ])
                {
                    // Сохраняем изменение: parent[rootI] = rootJ
                    SaveChange(rootI, _parent[rootI], true);
                    _parent[rootI] = rootJ;

                    // Сохраняем изменение: size[rootJ] += size[rootI]
                    SaveChange(rootJ, _rankOrSize[rootJ], false);
                    _rankOrSize[rootJ] += _rankOrSize[rootI];
                }
                else
                {
                    // Сохраняем изменение: parent[rootJ] = rootI
                    SaveChange(rootJ, _parent[rootJ], true);
                    _parent[rootJ] = rootI;

                    // Сохраняем изменение: size[rootI] += size[rootJ]
                    SaveChange(rootI, _rankOrSize[rootI], false);
                    _rankOrSize[rootI] += _rankOrSize[rootJ];
                }

                _components--;
                return true;
            }
            return false; // Уже в одном множестве
        }

        private void SaveChange(int element, int oldValue, bool isParentChange)
        {
            _history.Push(new Change
            {
                Element = element,
                OldValue = oldValue,
                IsParentChange = isParentChange
            });
        }

        // Отменяет последнюю операцию Union, восстанавливая массивы parent/rank/size
        public void Undo()
        {
            if (_history.Count == 0)
                throw new InvalidOperationException("Нет операций для отката");

            // Извлекаем маркер начала операции
            while (_history.Peek().Element != -1)
            {
                var change = _history.Pop();
                if (change.IsParentChange)
                {
                    _parent[change.Element] = change.OldValue;
                }
                else
                {
                    _rankOrSize[change.Element] = change.OldValue;
                }
            }

            // Извлекаем сам маркер
            _history.Pop();
            _components++; // Увеличиваем количество компонент (Union был откачен)
        }

        // --- Валидация индекса ---
        private void ValidateIndex(int i)
        {
            if (i < 0 || i >= _parent.Length)
                throw new ArgumentOutOfRangeException(nameof(i), $"Индекс {i} выходит за границы [0, {_parent.Length - 1}]");
        }

        // --- Получить корень компоненты (без сжатия пути, для отладки) ---
        public int GetRoot(int i)
        {
            ValidateIndex(i);
            return FindNoCompress(i);
        }

        // --- Сброс к начальному состоянию ---
        public void Reset()
        {
            _history.Clear();
            _components = _parent.Length;

            for (int i = 0; i < _parent.Length; i++)
            {
                _parent[i] = i;
                _rankOrSize[i] = (_strategy == UnionStrategy.BySize) ? 1 : 0;
            }
        }
    }

    // --- Тест Производительности и Функциональности ---

    public class DSUDemo
    {
        public static void RunTests()
        {
            Console.WriteLine("Тест 1: Union-Find с Эвристиками и Откатом");

            // Тест с Union by Rank
            Console.WriteLine("\nСтратегия: Union by Rank");
            TestWithStrategy(UnionStrategy.ByRank);

            // Тест с Union by Size
            Console.WriteLine("\nСтратегия: Union by Size");
            TestWithStrategy(UnionStrategy.BySize);
        }

        private static void TestWithStrategy(UnionStrategy strategy)
        {
            int n = 7;
            var dsu = new DSU(n, strategy);

            // Изначальное состояние
            Console.WriteLine($"Начальное состояние: {dsu.ComponentCount} компонент."); // 7

            // Union 1: Объединяем (0, 1)
            dsu.Union(0, 1);
            Console.WriteLine($"Union(0, 1): {dsu.ComponentCount} компонент."); // 6

            // Union 2: Объединяем (2, 3)
            dsu.Union(2, 3);
            Console.WriteLine($"Union(2, 3): {dsu.ComponentCount} компонент."); // 5

            // Union 3: Объединяем (1, 3) - 0, 1, 2, 3 теперь связаны
            dsu.Union(1, 3);
            Console.WriteLine($"Union(1, 3): {dsu.ComponentCount} компонент."); // 4

            // Find (сжатие пути):
            int root0 = dsu.Find(0);
            int root3 = dsu.Find(3);
            Console.WriteLine($"Find(0) -> {root0}, Find(3) -> {root3}."); // Должны быть одинаковые
            Console.WriteLine($"Connected(0, 3): {dsu.Connected(0, 3)}"); // True

            // Демонстрация размера/ранга
            if (strategy == UnionStrategy.BySize)
            {
                Console.WriteLine($"Размер компоненты 0: {dsu.GetComponentSize(0)}"); // 4
            }
            else
            {
                Console.WriteLine($"Ранг компоненты 0: {dsu.GetComponentRank(0)}"); // 1 или 2
            }

            // --- Тест Отката (Undo) ---
            Console.WriteLine("\n### Тест Отката (Undo)");
            Console.WriteLine($"Компонент до отката: {dsu.ComponentCount}"); // 4

            // Откат 1: Отмена Union(1, 3)
            dsu.Undo();
            Console.WriteLine($"Undo 1 (Union(1, 3)): {dsu.ComponentCount} компонент."); // 5

            // Проверка: 1 и 3 теперь снова в разных множествах
            Console.WriteLine($"Проверка Find(1) != Find(3) после отката: {dsu.Find(1) != dsu.Find(3)}"); // True

            // Откат 2: Отмена Union(2, 3)
            dsu.Undo();
            Console.WriteLine($"Undo 2 (Union(2, 3)): {dsu.ComponentCount} компонент."); // 6
        }

        // --- Тест Производительности (для 10^6 операций) ---
        public static void RunPerformanceTest()
        {
            Console.WriteLine("\n------------------------------------------------------------");
            Console.WriteLine("Тест 2: Производительность (10^6 элементов)");

            TestPerformanceWithStrategy(UnionStrategy.ByRank, "Union by Rank");
            TestPerformanceWithStrategy(UnionStrategy.BySize, "Union by Size");
        }

        private static void TestPerformanceWithStrategy(UnionStrategy strategy, string strategyName)
        {
            Console.WriteLine($"\n### Стратегия: {strategyName}");

            int n = 1_000_000;
            var dsu = new DSU(n, strategy);
            var random = new Random();
            int numOperations = 1_000_000;

            // 1. Тест Union и Find 
            var startTime = DateTime.Now;

            for (int i = 0; i < numOperations; i++)
            {
                int u = random.Next(n);
                int v = random.Next(n);

                if (i % 2 == 0)
                {
                    dsu.Union(u, v);
                }
                else
                {
                    dsu.Find(u);
                }
            }

            var opTime = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"Выполнено {numOperations} операций (Union/Find) за: {opTime:F2} мс");
            Console.WriteLine($"Среднее время на операцию: {(opTime / numOperations * 1_000_000):F2} нс");

            // 2. Тест Union и Undo
            int numUndoOps = 100_000;
            var dsuUndo = new DSU(n, strategy);
            startTime = DateTime.Now;

            for (int i = 0; i < numUndoOps; i++)
            {
                int u = random.Next(n);
                int v = random.Next(n);
                dsuUndo.Union(u, v);
                dsuUndo.Undo();
            }

            opTime = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"Выполнено {numUndoOps} пар (Union/Undo) за: {opTime:F2} мс");

            // 3. Тест Connected
            startTime = DateTime.Now;
            int connectedChecks = 100_000;
            for (int i = 0; i < connectedChecks; i++)
            {
                int u = random.Next(n);
                int v = random.Next(n);
                dsu.Connected(u, v);
            }
            opTime = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"Выполнено {connectedChecks} проверок Connected за: {opTime:F2} мс");
        }

        // --- Тест обработки ошибок ---
        public static void RunErrorHandlingTest()
        {
            Console.WriteLine("\n------------------------------------------------------------");
            Console.WriteLine("Тест 3: Обработка ошибок");

            try
            {
                var dsu = new DSU(5);
                dsu.Find(10); // Выход за границы
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine($"✓ Корректно обработан выход за границы: {ex.Message}");
            }

            try
            {
                var dsu = new DSU(5);
                dsu.Undo(); // Откат без операций
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"✓ Корректно обработан откат без операций: {ex.Message}");
            }

            try
            {
                var dsu = new DSU(5, UnionStrategy.ByRank);
                dsu.GetComponentSize(0); // Неправильный вызов для стратегии ByRank
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"✓ Корректно обработан неправильный вызов метода: {ex.Message}");
            }
        }
    }

    public class MainProgramDSU
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Запуск тестов системы непересекающихся множеств (DSU)");
            Console.WriteLine("========================================================\n");

            DSUDemo.RunTests();
            DSUDemo.RunPerformanceTest();
            DSUDemo.RunErrorHandlingTest();

            Console.WriteLine("\n========================================================");
            Console.WriteLine("Все тесты завершены успешно!");
        }
    }
}
