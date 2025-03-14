using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tool_Editor.maps.data
{
    public class Map
    {
        private static Dictionary<int, Map> _maps = new Dictionary<int, Map>();
        private bool _isLoaded;

        public int ID { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int NextRoom { get; set; }
        public int NextCell { get; set; }
        public bool IsEditing { get; set; }
        public CellsData[] Cells { get; set; }

        public static void AddMap(Map map)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            _maps[map.ID] = map;
        }

        public static Map GetMap(int id)
        {
            return _maps.ContainsKey(id) ? _maps[id] : null;
        }

        public void Load()
        {
            if (!_isLoaded)
            {
                // Logique de chargement ici
                _isLoaded = true;
            }
        }
    }
} 