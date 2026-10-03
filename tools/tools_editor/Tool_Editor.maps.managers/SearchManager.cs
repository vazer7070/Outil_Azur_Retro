using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tool_Editor.maps.data;

namespace Tool_Editor.maps.managers
{
    public static class SearchManager
    {
        public static Dictionary<int, Image> BGPicture = new Dictionary<int, Image>();
        public static List<string> Song = new List<string>();

        private static TreeNode CreateDirectoryNode(DirectoryInfo directory)
        {
            var node = new TreeNode(directory.Name) { Tag = directory.FullName };
            foreach (var child in directory.GetDirectories().OrderBy(entry => entry.Name))
                node.Nodes.Add(CreateDirectoryNode(child));
            return node;
        }

        public static Image ReturnBG(int key)
        {
            return BGPicture.TryGetValue(key, out Image image) ? image : null;
        }

        public static bool IsNumeric(this string input) { return int.TryParse(input, out _); }

        internal static bool IsImage(string path)
        {
            string extension = Path.GetExtension(path);
            return new[] { ".png", ".jpg", ".jpeg", ".bmp" }
                .Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public static void SearchBackground(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.EnumerateFiles(path).OrderBy(name => name))
                if (IsImage(file) && int.TryParse(Path.GetFileNameWithoutExtension(file), out int id) &&
                    id >= 0 && id < TilesData.Backgrounds_Tiles.Length)
                    TilesData.Backgrounds_Tiles[id] = new TilesData(id, Path.GetFullPath(file), "", TilesData.TileType.background);
        }

        public static void SearchGrounds(string path, TreeNode node)
        {
            SearchTiles(path, node, TilesData.ListGrounds, TilesData.TileType.ground);
        }

        public static void SearchObject(string path, TreeNode node)
        {
            SearchTiles(path, node, TilesData.ListObject, TilesData.TileType.objet);
        }

        private static void SearchTiles(string path, TreeNode node, TilesData[] registry, TilesData.TileType type)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            node.Nodes.Clear();
            if (!Directory.Exists(path)) return;
            var directory = new DirectoryInfo(path);
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(entry => entry.FullName))
                if (IsImage(file.FullName) && int.TryParse(Path.GetFileNameWithoutExtension(file.Name), out int id) &&
                    id >= 0 && id < registry.Length)
                    registry[id] = new TilesData(id, file.FullName, file.Directory.Name, type);
            node.Nodes.Add(CreateDirectoryNode(directory));
        }

        public static void SearchZik(string path)
        {
            Song.Clear();
            if (!Directory.Exists(path)) return;
            Song.AddRange(Directory.EnumerateFiles(path).Select(Path.GetFileNameWithoutExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name));
        }
    }
}
