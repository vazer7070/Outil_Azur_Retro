using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Tool_Editor.maps.data;

namespace Tool_Editor.maps.managers
{
	public static class SearchManager
	{
		public static Dictionary<int, Image> BGPicture;

		public static List<string> Song;

		static SearchManager()
		{
            BGPicture = new Dictionary<int, Image>();
            Song = new List<string>();
		}

		

		private static TreeNode CreateDirectoryNode(DirectoryInfo directoryInfo)
		{
			TreeNode directoryNode = new TreeNode(directoryInfo.Name);
			DirectoryInfo[] directories = directoryInfo.GetDirectories();
			for (int i = 0; i < (int)directories.Length; i++)
			{
				DirectoryInfo directory = directories[i];
				directoryNode.Nodes.Add(CreateDirectoryNode(directory));
			}
			return directoryNode;
		}

		public static Image ReturnBG(int keys)
		{
			KeyValuePair<int, Image> keyValuePair = SearchManager.BGPicture.FirstOrDefault<KeyValuePair<int, Image>>((KeyValuePair<int, Image> x) => x.Key == keys);
			return keyValuePair.Value;
		}
		public static bool IsNumeric(this string input)
		{
			int number;
			return int.TryParse(input, out number);
		}
		public static void SearchBackground(string path)
		{
			FileInfo[] files = (new DirectoryInfo(path)).GetFiles();
			foreach(FileInfo F in files)
            {
				if(F.Extension == ".jpg" || F.Extension == ".png" || F.Extension == ".jpeg" || F.Extension == ".bmp")
                {
					string S_id = F.Name.Split('.')[0];
                    if (!IsNumeric(S_id))
                    {
						MessageBox.Show($"Le fichier {S_id} n'est pas un fichier valide, suppression.", "Fichier invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						File.Delete(F.FullName);
                    }
                    else
                    {
						int Id = Convert.ToInt32(S_id);
                        try
                        {
							TilesData G = new TilesData(Id, F.FullName, "" ,TilesData.TileType.background);
							TilesData.Backgrounds_Tiles[G.ID] = G;
                        }
                        catch
                        {
							MessageBox.Show($"Le fond avec l'id: {Id} est présent en double, suppression", "Fond en double", MessageBoxButtons.OK, MessageBoxIcon.Warning);
							//File.Delete(F.FullName);
                        }
                    }
                }
            }
		}

		public static void SearchGrounds(string path, TreeNode Node)
		{
			int r;
			TreeNode NN = new TreeNode();
			DirectoryInfo D = new DirectoryInfo(path);
			string[] sub = Directory.GetDirectories(path);
			foreach(string h in sub)
            {
				string[] p = Directory.GetFiles(h);
				foreach(string k in p)
                {
					if(k.Contains(".png") || k.Contains(".jpg") || k.Contains(".jpeg") || k.Contains(".bmp"))
                    {
						
						r = Convert.ToInt32(k.Split('.')[1].Split('\\')[5]);
						string m = k.Split('.')[1].Split('\\')[4].Split('\\')[0];
						TilesData.ListGrounds[r] = new TilesData(r, k, m, TilesData.TileType.ground);
						
					}
                }
				
			}
			
			
			Node.Nodes.Add(CreateDirectoryNode(D));
		}

		public static void SearchObject(string path, TreeNode Node)
		{
			int r;
			TreeNode NN = new TreeNode();
			DirectoryInfo D = new DirectoryInfo(path);
			FileInfo[] files = D.GetFiles();
			for (int i = 0; i < (int)files.Length; i++)
			{
				FileInfo F = files[i];
				if ((F.Extension == ".png" || F.Extension == ".jpg" || F.Extension == ".jpeg" ? true : F.Extension == ".bmp"))
				{
					if (int.TryParse(F.Name.Split(new char[] { '.' })[0], out r))
					{
						TilesData.ListObject[r] = new TilesData(r, F.FullName, D.Name, TilesData.TileType.objet);
					}
				}
			}
			Node.Nodes.Add(CreateDirectoryNode(D));
		}

		public static void SearchZik(string path)
		{
			FileInfo[] files = (new DirectoryInfo(path)).GetFiles();
			for (int i = 0; i < (int)files.Length; i++)
			{
				FileInfo e = files[i];
                Song.Add(e.Name.Split(new char[] { '.' })[0]);
			}
		}
	}
}