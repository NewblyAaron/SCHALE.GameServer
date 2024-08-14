using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace SCHALE.GameServer.Utils
{
    public class GachaProbability
    {
        public static long Random(Dictionary<Common.FlatData.CharacterExcelT, double> pool, double minUnit = 0.01)
        {
            List<Common.FlatData.CharacterExcelT> allItem = [];
            foreach (var k in pool)
            {
                for (int i = 0; i < Math.Floor(pool[k.Key] / minUnit); i++)
                {
                    allItem.Add(k.Key);
                }
            }

            var index = Convert.ToInt32(Math.Floor(System.Random.Shared.NextDouble() * allItem.Count));
            return allItem[index].Id;
        }
    }
}