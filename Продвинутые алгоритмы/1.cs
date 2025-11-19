using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    public class KnapsackSolver
    {
        public struct Item
        {
            public int Weight;
            public int Value;
            public int Index;
        }

        public class KnapsackResult
        {
            public int MaxValue { get; set; }
            public Listint SelectedIndices { get; set; }
            public ListItem SelectedItems { get; set; }

            public override string ToString()
            {
                var indices = string.Join(, , SelectedIndices.Select(i = i + 1));
                return $Максимальная ценность {MaxValue}nВыбранные предметы (индексы) [{indices}];
            }
        }

         1.1 Стандартный 01 Knapsack
        public KnapsackResult Solve01(int[] weights, int[] values, int capacity)
        {
            int n = weights.Length;
            Item[] items = new Item[n];
            for (int i = 0; i  n; i++)
            {
                items[i] = new Item { Weight = weights[i], Value = values[i], Index = i };
            }

            int[,] dp = new int[n + 1, capacity + 1];

             Заполнение таблицы DP
            for (int i = 1; i = n; i++)
            {
                int w_i = items[i - 1].Weight;
                int v_i = items[i - 1].Value;

                for (int w = 0; w = capacity; w++)
                {
                    if (w  w_i)
                    {
                        dp[i, w] = dp[i - 1, w];
                    }
                    else
                    {
                        dp[i, w] = Math.Max(dp[i - 1, w], v_i + dp[i - 1, w - w_i]);
                    }
                }
            }

             Восстановление решения
            var result = new KnapsackResult
            {
                MaxValue = dp[n, capacity],
                SelectedIndices = new Listint(),
                SelectedItems = new ListItem()
            };

            int currentCapacity = capacity;
            for (int i = n; i  0 && currentCapacity  0; i--)
            {
                if (dp[i, currentCapacity] != dp[i - 1, currentCapacity])
                {
                    var item = items[i - 1];
                    result.SelectedIndices.Add(item.Index);
                    result.SelectedItems.Add(item);
                    currentCapacity -= item.Weight;
                }
            }

            result.SelectedIndices.Sort();
            return result;
        }

         1.2 Bounded Knapsack
        public KnapsackResult SolveBounded(int[] weights, int[] values, int[] counts, int capacity)
        {
            var items01 = new ListItem();
            int originalIndex = 0;

             Бинарное кодирование для преобразования в 01 Knapsack
            for (int i = 0; i  weights.Length; i++)
            {
                int w = weights[i];
                int v = values[i];
                int c = counts[i];
                int k = 1;

                while (c  0)
                {
                    int take = Math.Min(k, c);
                    items01.Add(new Item { Weight = w  take, Value = v  take, Index = originalIndex });
                    c -= take;
                    k = 2;
                }
                originalIndex++;
            }

             Решаем как 01 рюкзак с трассировкой
            int n = items01.Count;
            int[,] dp = new int[n + 1, capacity + 1];
            bool[,] taken = new bool[n + 1, capacity + 1];

             Заполнение DP с отметкой взятых предметов
            for (int i = 1; i = n; i++)
            {
                var item = items01[i - 1];
                for (int w = 0; w = capacity; w++)
                {
                    if (w  item.Weight)
                    {
                        dp[i, w] = dp[i - 1, w];
                        taken[i, w] = false;
                    }
                    else
                    {
                        int valueWithout = dp[i - 1, w];
                        int valueWith = item.Value + dp[i - 1, w - item.Weight];

                        if (valueWith  valueWithout)
                        {
                            dp[i, w] = valueWith;
                            taken[i, w] = true;
                        }
                        else
                        {
                            dp[i, w] = valueWithout;
                            taken[i, w] = false;
                        }
                    }
                }
            }

             Восстановление решения
            var result = new KnapsackResult
            {
                MaxValue = dp[n, capacity],
                SelectedIndices = new Listint(),
                SelectedItems = new ListItem()
            };

            int currentCap = capacity;
            for (int i = n; i  0 && currentCap  0; i--)
            {
                if (taken[i, currentCap])
                {
                    var compositeItem = items01[i - 1];
                     Добавляем исходный индекс (может повторяться для bounded)
                    result.SelectedIndices.Add(compositeItem.Index);
                    result.SelectedItems.Add(new Item
                    {
                        Weight = weights[compositeItem.Index],
                        Value = values[compositeItem.Index],
                        Index = compositeItem.Index
                    });
                    currentCap -= compositeItem.Weight;
                }
            }

            result.SelectedIndices.Sort();
            return result;
        }

        1.3 Unbounded Knapsack
        public KnapsackResult SolveUnbounded(int[] weights, int[] values, int capacity)
        {
            int n = weights.Length;
            int[] dp = new int[capacity + 1];
            int[] lastItem = new int[capacity + 1];  Для трассировки

            for (int w = 0; w = capacity; w++)
            {
                for (int i = 0; i  n; i++)
                {
                    if (w = weights[i])
                    {
                        if (dp[w]  dp[w - weights[i]] + values[i])
                        {
                            dp[w] = dp[w - weights[i]] + values[i];
                            lastItem[w] = i;  Запоминаем, какой предмет добавили
                        }
                    }
                }
            }

             Восстановление решения
            var result = new KnapsackResult
            {
                MaxValue = dp[capacity],
                SelectedIndices = new Listint(),
                SelectedItems = new ListItem()
            };

            int currentCapacity = capacity;
            while (currentCapacity  0 && dp[currentCapacity]  0)
            {
                int itemIndex = lastItem[currentCapacity];
                result.SelectedIndices.Add(itemIndex);
                result.SelectedItems.Add(new Item
                {
                    Weight = weights[itemIndex],
                    Value = values[itemIndex],
                    Index = itemIndex
                });
                currentCapacity -= weights[itemIndex];
            }

            result.SelectedIndices.Sort();
            return result;
        }
    }

    public class KnapsackDemo
    {
        public static void RunTests()
        {
            var solver = new KnapsackSolver();

            Console.WriteLine( Тест 1 - Стандартный 01 Knapsack);
            int[] weights01 = { 2, 3, 4, 5 };
            int[] values01 = { 3, 4, 5, 6 };
            int capacity01 = 5;

            Console.WriteLine($Вход Weights=[{string.Join(,, weights01)}], Values=[{string.Join(,, values01)}], Capacity={capacity01});

            var result01 = solver.Solve01(weights01, values01, capacity01);
            Console.WriteLine($Ожидаемый выход maxValue=7, items=[0, 1]);
            Console.WriteLine($Полученный результатn{result01});

            Console.WriteLine(n Тест 2 - Unbounded Knapsack);
            int[] weightsUn = { 2, 3 };
            int[] valuesUn = { 4, 6 };
            int capacityUn = 5;

            Console.WriteLine($Вход Weights=[{string.Join(,, weightsUn)}], Values=[{string.Join(,, valuesUn)}], Capacity={capacityUn});

            var resultUn = solver.SolveUnbounded(weightsUn, valuesUn, capacityUn);
            Console.WriteLine(Ожидаемый выход maxValue=10, items=[0, 1]);
            Console.WriteLine($Полученный результатn{resultUn});

            Console.WriteLine(n Тест 3 - Bounded Knapsack);
            int[] weightsBound = { 2, 3 };
            int[] valuesBound = { 4, 6 };
            int[] countsBound = { 1, 2 };
            int capacityBound = 5;

            Console.WriteLine($Вход Weights=[{string.Join(,, weightsBound)}], Values=[{string.Join(,, valuesBound)}], Counts=[{string.Join(,, countsBound)}], Capacity={capacityBound});

            var resultBound = solver.SolveBounded(weightsBound, valuesBound, countsBound, capacityBound);
            Console.WriteLine(Ожидаемый выход maxValue=10, items=[0, 1]);
            Console.WriteLine($Полученный результатn{resultBound});
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            KnapsackDemo.RunTests();
            Console.WriteLine(nНажмите любую клавишу для выхода...);
            Console.ReadKey();
        }
    }
}