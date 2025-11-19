using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    // 1. Узлы Суффиксного Дерева 
    public class SuffixNode
    {
        // Словарь переходов
        public Dictionary<int, SuffixNode> Children { get; } = new Dictionary<int, SuffixNode>();

        // Индекс начала ребра в исходной строке (start)
        // Индекс конца ребра в исходной строке (end)
        public int Start { get; set; }
        public int End { get; set; }

        // Ссылка на суффиксную ссылку 
        public SuffixNode SuffixLink { get; set; }

        // Индекс суффикса 
        public int SuffixIndex { get; set; } = -1;

        public SuffixNode(int start, int end)
        {
            Start = start;
            End = end;
        }

        // Длина ребра
        public int EdgeLength(int position)
        {
            if (End == -1) return position - Start + 1;
            return End - Start + 1;
        }
    }

    // 2. Класс Суффиксного Дерева 
    public class SuffixTree
    {
        private readonly int[] _text;
        private readonly int _n;
        private readonly SuffixNode _root;

        // Активная точка для алгоритма Укконена
        private SuffixNode _activeNode;
        private int _activeEdge = -1;
        private int _activeLength = 0;
        private int _remainder = 0;
        private int _leafEnd = -1;

        // Символы-терминаторы
        private const int TERMINATOR1 = -1;
        private const int TERMINATOR2 = -2;
        private const int SEPARATOR = -3;

        public SuffixTree(string text)
        {
            // Конвертируем строку в массив кодовых точек для поддержки Unicode
            _text = StringToCodePoints(text);
            _n = _text.Length;
            _root = new SuffixNode(-1, -1);
            _activeNode = _root;

            BuildUkkonen();
        }

        // Конструктор для обобщенного суффиксного дерева
        private SuffixTree(int[] text)
        {
            _text = text;
            _n = text.Length;
            _root = new SuffixNode(-1, -1);
            _activeNode = _root;

            BuildUkkonen();
        }

        // 2.1. Конвертация строки в кодовые точки 
        private static int[] StringToCodePoints(string str)
        {
            var codePoints = new List<int>();
            for (int i = 0; i < str.Length; i++)
            {
                int codePoint = char.ConvertToUtf32(str, i);
                codePoints.Add(codePoint);
                if (char.IsSurrogatePair(str, i))
                    i++;
            }
            return codePoints.ToArray();
        }

        private static string CodePointsToString(int[] codePoints, int start, int length)
        {
            var sb = new StringBuilder();
            for (int i = start; i < start + length; i++)
            {
                if (codePoints[i] >= 0)
                {
                    sb.Append(char.ConvertFromUtf32(codePoints[i]));
                }
            }
            return sb.ToString();
        }

        //  2.2. Алгоритм Укконена 
        private void BuildUkkonen()
        {
            for (int i = 0; i < _n; i++)
            {
                ExtendSuffixTree(i);
            }

            // Устанавливаем индексы суффиксов
            SetSuffixIndices(_root, 0);
        }

        private void ExtendSuffixTree(int pos)
        {
            _leafEnd = pos;
            _remainder++;
            SuffixNode lastNewNode = null;

            while (_remainder > 0)
            {
                if (_activeLength == 0)
                    _activeEdge = pos;

                if (!_activeNode.Children.ContainsKey(_text[_activeEdge]))
                {
                    // Правило 2: создаем новый лист
                    _activeNode.Children[_text[_activeEdge]] = new SuffixNode(pos, -1);

                    if (lastNewNode != null)
                    {
                        lastNewNode.SuffixLink = _activeNode;
                        lastNewNode = null;
                    }
                }
                else
                {
                    SuffixNode next = _activeNode.Children[_text[_activeEdge]];
                    int edgeLength = next.EdgeLength(pos);

                    if (_activeLength >= edgeLength)
                    {
                        _activeEdge += edgeLength;
                        _activeLength -= edgeLength;
                        _activeNode = next;
                        continue;
                    }

                    if (_text[next.Start + _activeLength] == _text[pos])
                    {
                        // Правило 3: продление
                        if (lastNewNode != null && _activeNode != _root)
                        {
                            lastNewNode.SuffixLink = _activeNode;
                        }
                        _activeLength++;
                        break;
                    }

                    // Правило 2: разделение
                    SuffixNode split = new SuffixNode(next.Start, next.Start + _activeLength - 1);
                    _activeNode.Children[_text[_activeEdge]] = split;

                    split.Children[_text[pos]] = new SuffixNode(pos, -1);
                    next.Start += _activeLength;
                    split.Children[_text[next.Start]] = next;

                    if (lastNewNode != null)
                    {
                        lastNewNode.SuffixLink = split;
                    }

                    lastNewNode = split;
                }

                _remainder--;

                if (_activeNode == _root && _activeLength > 0)
                {
                    _activeLength--;
                    _activeEdge = pos - _remainder + 1;
                }
                else if (_activeNode != _root)
                {
                    _activeNode = _activeNode.SuffixLink ?? _root;
                }
            }
        }

        private void SetSuffixIndices(SuffixNode node, int labelHeight)
        {
            if (node == null) return;

            bool isLeaf = true;
            foreach (var child in node.Children.Values)
            {
                isLeaf = false;
                int childEdgeLength = child.End == -1 ? _n - child.Start : child.End - child.Start + 1;
                SetSuffixIndices(child, labelHeight + childEdgeLength);
            }

            if (isLeaf)
            {
                node.SuffixIndex = _n - labelHeight;
            }
        }

        // 3. Поиск всех вхождений паттерна 
        public List<int> SearchPattern(string pattern)
        {
            int[] patternCodes = StringToCodePoints(pattern);
            SuffixNode node = TraversePattern(_root, patternCodes);

            if (node == null)
                return new List<int>();

            return CollectLeafIndices(node);
        }

        private SuffixNode TraversePattern(SuffixNode startNode, int[] pattern)
        {
            SuffixNode currentNode = startNode;
            int patternIndex = 0;

            while (patternIndex < pattern.Length)
            {
                int currentChar = pattern[patternIndex];
                if (!currentNode.Children.ContainsKey(currentChar))
                    return null;

                SuffixNode child = currentNode.Children[currentChar];
                int edgeLength = child.End == -1 ? _n - child.Start : child.End - child.Start + 1;
                int matchLength = Math.Min(edgeLength, pattern.Length - patternIndex);

                for (int i = 0; i < matchLength; i++)
                {
                    if (_text[child.Start + i] != pattern[patternIndex + i])
                        return null;
                }

                patternIndex += matchLength;
                currentNode = child;
            }

            return currentNode;
        }

        // Вспомогательная функция для сбора всех индексов суффиксов в поддереве
        private List<int> CollectLeafIndices(SuffixNode node)
        {
            var indices = new HashSet<int>();
            var stack = new Stack<SuffixNode>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                SuffixNode current = stack.Pop();

                if (current.SuffixIndex != -1)
                {
                    indices.Add(current.SuffixIndex);
                }

                foreach (var child in current.Children.Values)
                {
                    stack.Push(child);
                }
            }

            return indices.ToList();
        }

        // 4. Поиск самой длинной повторяющейся подстроки
        public string FindLongestRepeatedSubstring()
        {
            int maxLength = 0;
            int startIndex = -1;

            FindLrsRecursive(_root, 0, ref maxLength, ref startIndex);

            if (maxLength > 0 && startIndex != -1)
            {
                return CodePointsToString(_text, startIndex, maxLength);
            }

            return "";
        }

        private int FindLrsRecursive(SuffixNode node, int pathLength, ref int maxLength, ref int startIndex)
        {
            if (node == null) return 0;

            // Если это внутренний узел
            if (node.Children.Count > 0)
            {
                int totalLeaves = 0;

                foreach (var child in node.Children.Values)
                {
                    int childEdgeLength = child.End == -1 ? _n - child.Start : child.End - child.Start + 1;
                    totalLeaves += FindLrsRecursive(child, pathLength + childEdgeLength, ref maxLength, ref startIndex);
                }

                // Если у узла есть хотя бы 2 листа в поддереве
                if (totalLeaves >= 2 && pathLength > maxLength)
                {
                    maxLength = pathLength;
                    // Вычисляем начало подстроки
                    if (node != _root)
                    {
                        startIndex = node.SuffixIndex != -1 ? node.SuffixIndex : FindFirstLeaf(node).SuffixIndex;
                        startIndex = startIndex - pathLength;
                    }
                }

                return totalLeaves;
            }
            else
            {
                // Листовой узел
                return 1;
            }
        }

        private SuffixNode FindFirstLeaf(SuffixNode node)
        {
            while (node.Children.Count > 0)
            {
                node = node.Children.Values.First();
            }
            return node;
        }

        // 5. Поиск наибольшей общей подстроки двух строк 
        public static string FindLongestCommonSubstring(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return "";

            // Конвертируем строки в кодовые точки
            int[] s1Codes = StringToCodePoints(s1);
            int[] s2Codes = StringToCodePoints(s2);

            // Создаем объединенный текст с разделителями
            var combined = new List<int>();
            combined.AddRange(s1Codes);
            combined.Add(SEPARATOR);
            combined.AddRange(s2Codes);
            combined.Add(TERMINATOR1);

            // Строим обобщенное суффиксное дерево
            var gst = new SuffixTree(combined.ToArray());

            // Ищем наибольшую общую подстроку
            int maxLength = 0;
            int startIndex = -1;
            gst.FindLcsRecursive(gst._root, 0, ref maxLength, ref startIndex, s1Codes.Length);

            if (maxLength > 0 && startIndex != -1)
            {
                return CodePointsToString(combined.ToArray(), startIndex, maxLength);
            }

            return "";
        }

        private void FindLcsRecursive(SuffixNode node, int pathLength, ref int maxLength, ref int startIndex, int s1Length)
        {
            if (node == null) return;

            // Проверяем, содержит ли поддерево суффиксы из обеих строк
            var flags = CheckSubtreeFlags(node, s1Length);

            // Если поддерево содержит суффиксы из обеих строк
            if ((flags & 3) == 3) // 1 (s1) | 2 (s2) = 3
            {
                if (pathLength > maxLength && node != _root)
                {
                    maxLength = pathLength;
                    startIndex = FindFirstLeaf(node).SuffixIndex - pathLength;
                }

                // Рекурсивно проверяем детей
                foreach (var child in node.Children.Values)
                {
                    int childEdgeLength = child.End == -1 ? _n - child.Start : child.End - child.Start + 1;
                    FindLcsRecursive(child, pathLength + childEdgeLength, ref maxLength, ref startIndex, s1Length);
                }
            }
        }

        private int CheckSubtreeFlags(SuffixNode node, int s1Length)
        {
            var stack = new Stack<SuffixNode>();
            stack.Push(node);
            int flags = 0;

            while (stack.Count > 0)
            {
                SuffixNode current = stack.Pop();

                if (current.SuffixIndex != -1)
                {
                    if (current.SuffixIndex <= s1Length)
                        flags |= 1; // Принадлежит первой строке
                    else if (current.SuffixIndex > s1Length + 1) // +1 для разделителя
                        flags |= 2; // Принадлежит второй строке
                }

                foreach (var child in current.Children.Values)
                {
                    stack.Push(child);
                }

                // Если уже нашли обе метки, можно выходить
                if ((flags & 3) == 3)
                    break;
            }

            return flags;
        }

        // 6. Оптимизация памяти - компактное представление
        public long GetMemoryUsage()
        {
            var visited = new HashSet<SuffixNode>();
            return CalculateMemoryUsage(_root, visited);
        }

        private long CalculateMemoryUsage(SuffixNode node, HashSet<SuffixNode> visited)
        {
            if (node == null || visited.Contains(node)) return 0;
            visited.Add(node);

            long size = sizeof(int) * 4; // Start, End, SuffixIndex
            size += IntPtr.Size * 2; // SuffixLink и ссылка на словарь
            size += node.Children.Count * (sizeof(int) + IntPtr.Size) * 2; // Dictionary entries

            foreach (var child in node.Children.Values)
            {
                size += CalculateMemoryUsage(child, visited);
            }

            return size;
        }
    }

    // 7. Демонстрация и Тесты 
    public class SuffixTreeDemo
    {
        public static void RunTests()
        {
            Console.WriteLine("Демонстрация: Суффиксное Дерево (Алгоритм Укконена)");

            // Тест 1: Базовый поиск
            string text = "abracadabra";
            var tree = new SuffixTree(text);

            string pattern = "bra";
            var indices = tree.SearchPattern(pattern);
            Console.WriteLine($"\n1. Поиск паттерна '{pattern}'");
            Console.WriteLine($"Текст: {text}");
            Console.WriteLine($"Вхождения: [{string.Join(", ", indices)}]");
            Console.WriteLine($"Ожидаемо: [1, 8] | Получено: [{string.Join(", ", indices)}]");

            // Тест 2: Самая длинная повторяющаяся подстрока
            string lrs = tree.FindLongestRepeatedSubstring();
            Console.WriteLine($"\n2. Самая длинная повторяющаяся подстрока");
            Console.WriteLine($"Результат: '{lrs}'");
            Console.WriteLine($"Ожидаемо: 'abra' | Получено: '{lrs}'");

            // Тест 3: Наибольшая общая подстрока
            string s1 = "banana";
            string s2 = "cabana";
            string lcs = SuffixTree.FindLongestCommonSubstring(s1, s2);
            Console.WriteLine($"\n3. Наибольшая общая подстрока (LCS)");
            Console.WriteLine($"Строки: '{s1}', '{s2}'");
            Console.WriteLine($"Результат: '{lcs}'");
            Console.WriteLine($"Ожидаемо: 'bana' | Получено: '{lcs}'");

            // Тест 4: Поддержка Unicode
            string unicodeText = "Hello 世界  Привет";
            var unicodeTree = new SuffixTree(unicodeText);
            string unicodePattern = "世界";
            var unicodeIndices = unicodeTree.SearchPattern(unicodePattern);
            Console.WriteLine($"\n 4. Поддержка Unicode");
            Console.WriteLine($"Текст: {unicodeText}");
            Console.WriteLine($"Поиск '{unicodePattern}': [{string.Join(", ", unicodeIndices)}]");

            // Тест 5: Производительность
            Console.WriteLine($"\n 5. Использование памяти");
            Console.WriteLine($"Приблизительное использование памяти: {tree.GetMemoryUsage()} байт");

            // Тест 6: Большой текст (демонстрация O(n))
            Console.WriteLine($"\n 6. Тест производительности");
            TestPerformance();
        }

        private static void TestPerformance()
        {
            // Генерируем большой текст для демонстрации O(n)
            var rnd = new Random(42);
            const int size = 100000; // 100K символов
            var bigText = new StringBuilder(size);

            for (int i = 0; i < size; i++)
            {
                bigText.Append((char)('a' + rnd.Next(0, 26)));
            }

            Console.WriteLine($"Построение дерева для {size:N0} символов...");
            var startTime = DateTime.Now;

            var bigTree = new SuffixTree(bigText.ToString());

            var buildTime = DateTime.Now - startTime;
            Console.WriteLine($"Время построения: {buildTime.TotalMilliseconds} мс");

            // Поиск в большом тексте
            startTime = DateTime.Now;
            var results = bigTree.SearchPattern("abc");
            var searchTime = DateTime.Now - startTime;

            Console.WriteLine($"Время поиска: {searchTime.TotalMilliseconds} мс");
            Console.WriteLine($"Найдено вхождений: {results.Count}");
        }
    }

    public class MainProgramSuffix
    {
        public static void Main(string[] args)
        {
            SuffixTreeDemo.RunTests();

            // Дополнительный тест: интерактивный поиск
            Console.WriteLine("\nИнтерактивный поиск (введите 'exit' для выхода):");

            string baseText = "abracadabra";
            var interactiveTree = new SuffixTree(baseText);

            while (true)
            {
                Console.Write($"Поиск в тексте '{baseText}': ");
                string input = Console.ReadLine();

                if (input?.ToLower() == "exit") break;

                if (!string.IsNullOrEmpty(input))
                {
                    var results = interactiveTree.SearchPattern(input);
                    if (results.Count > 0)
                    {
                        Console.WriteLine($"Найдено на позициях: [{string.Join(", ", results)}]");
                    }
                    else
                    {
                        Console.WriteLine("Не найдено");
                    }
                }
            }
        }
    }
}