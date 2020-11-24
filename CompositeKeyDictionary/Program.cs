using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompositeKeyDictionary
{
    class Program
    {
        static void Main(string[] args)
        {
            //Console.ReadKey();

            //Random rnd = new Random();

            //List<CompositeKey<int, int>> keys = Enumerable.Range(1, 10000000)
            //    .Select(i => new CompositeKey<int, int>(rnd.Next(1, 1000), i))
            //    .ToList();

            //CompositeKeyDictionary<int, int, int> compositeDict = new CompositeKeyDictionary<int, int, int>();
            //Dictionary<CompositeKey<int, int>, int> dict = new Dictionary<CompositeKey<int, int>, int>();

            //int num = 0;
            //keys.ForEach(key =>
            //{
            //    compositeDict.Add(key, num);
            //    dict.Add(key, num++);
            //});

            //Stopwatch stopwatch = new Stopwatch();

            //stopwatch.Start();
            //if (compositeDict.TryGetFirstKeyValues(10, out IEnumerable<int> vals))
            //{
            //    stopwatch.Stop();
            //    Console.WriteLine($"Count - {vals.Count()},  elapsed - {stopwatch.ElapsedMilliseconds}");
            //}


            //stopwatch.Restart();

            //var list = dict.Where(kvp => kvp.Key.KeyA == 10).Count();
            //stopwatch.Stop();

            //Console.WriteLine($"Count - {list},  elapsed - {stopwatch.ElapsedMilliseconds}");

            Dictionary<MyClass, string> dict = new Dictionary<MyClass, string>();
            dict.Add(new MyClass() { ItemID = 5, ListID = 10 }, "Hi");
            dict.Add(new MyClass() { ItemID = 10, ListID = 5 }, "Hello");

            Console.WriteLine(dict.Count);
            Console.WriteLine(dict.Keys.Count);
          
        }

        public class MyClass
        {
            public int ItemID;
            public int ListID;

            public override bool Equals(object obj)
            {
                return obj is MyClass @class &&
                       ItemID == @class.ItemID &&
                       ListID == @class.ListID;
            }

            public override int GetHashCode()
            {
                return ItemID + ListID;
            }
        }
    }
}
