﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumPanelColorTiles.Utils.ColorAreaTable
{
    class DataRow
    {
        public string Size;
        public Dictionary<ColorArea, string> LayersCount { get; set; }
        public Tuple<int, double> Area { get; set; }

        public DataRow (string size, Dictionary<ColorArea, List<ColorArea>> items, int totalAreas)
        {
            Size = size;            
            CalcArea(items, totalAreas);
        }

        private void CalcArea (Dictionary<ColorArea, List<ColorArea>> items, int totalAreas)
        {
            double areaAll = 0;
            int countAll = 0;
            LayersCount = new Dictionary<ColorArea, string>();
            foreach (var item in items)
            {
                var area = GetArea(item.Value);
                // %                
                var percent =Math.Round(item.Value.Count * 100d / totalAreas,1);
                var cell = $"{item.Value.Count} ({area}м{AcadLib.General.Symbols.Square}) {percent}%";
                LayersCount.Add(item.Key, cell);
                areaAll += area;
                countAll += item.Value.Count;
            }
            Area = new Tuple<int, double>(countAll, areaAll);
        }

        private double GetArea (List<ColorArea> items)
        {
            var areaOneTile = items.First().Area;
            var count = items.Count;
            var areaAllTiles = areaOneTile * count;            
            return areaAllTiles;
        }
    }
}
