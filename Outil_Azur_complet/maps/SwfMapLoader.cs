using SwfDotNet.IO;
using SwfDotNet.IO.Tags;
using SwfDotNet.IO.ByteCode;
using System;
using System.IO;
using System.Drawing;
using Tool_Editor.maps.data;

namespace Outil_Azur_complet.maps
{
    public class SwfMapLoader
    {
        private SwfReader swfReader;
        private Swf swfFile;
        private Map targetMap;

        public bool LoadFromFile(string filePath, Map map)
        {
            try
            {
                // Initialisation du lecteur SWF
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    swfReader = new SwfReader(fs);
                    swfFile = swfReader.ReadSwf();
                    targetMap = map;

                    // Extraire les dimensions de la carte
                    ExtractMapDimensions();
                    
                    // Parcourir les tags pour extraire les données
                    foreach (BaseTag tag in swfFile.Tags)
                    {
                        switch (tag.TagCode)
                        {
                            case TagCodeEnum.DefineBitsJPEG2:
                            case TagCodeEnum.DefineBitsJPEG3:
                                LoadBackground(tag);
                                break;

                            case TagCodeEnum.DefineShape:
                            case TagCodeEnum.DefineShape2:
                            case TagCodeEnum.DefineShape3:
                                LoadTile(tag);
                                break;

                            case TagCodeEnum.PlaceObject2:
                                PlaceObject2Tag placeTag = tag as PlaceObject2Tag;
                                if (placeTag != null)
                                {
                                    ProcessPlaceObject(placeTag);
                                }
                                break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Erreur lors du chargement du SWF : {ex.Message}",
                    "Erreur", 
                    System.Windows.Forms.MessageBoxButtons.OK, 
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        private void ExtractMapDimensions()
        {
            // Les dimensions sont dans l'en-tête du SWF
            Rectangle rect = swfFile.Header.FrameSize;
            targetMap.Width = rect.Width / CellsData.SizeCell;
            targetMap.Height = rect.Height / CellsData.SizeCell;
            
            // Initialiser le tableau de cellules avec les nouvelles dimensions
            targetMap.Cells = new CellsData[(targetMap.Height * (targetMap.Width * 2 - 1) - targetMap.Width + 1)];
        }

        private void LoadBackground(BaseTag tag)
        {
            try
            {
                if (tag is DefineBitsJPEG2Tag jpeg2Tag)
                {
                    // Convertir les données JPEG en Bitmap
                    using (MemoryStream ms = new MemoryStream(jpeg2Tag.BitmapData))
                    {
                        Bitmap bmp = new Bitmap(ms);
                        TilesData background = new TilesData();
                        background.ImageLoaded = bmp;
                        background.ID = jpeg2Tag.CharacterId;
                        targetMap.Background = background;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Erreur lors du chargement du fond : {ex.Message}");
            }
        }

        private void LoadTile(BaseTag tag)
        {
            try
            {
                if (tag is DefineShapeTag shapeTag)
                {
                    // Créer une nouvelle tuile
                    TilesData tile = new TilesData();
                    tile.ID = shapeTag.CharacterId;

                    // Déterminer le type de tuile basé sur les propriétés de la forme
                    var shapeWithStyle = shapeTag.ShapeWithStyle;
                    if (shapeWithStyle != null && shapeWithStyle.FillStyles.Count > 0)
                    {
                        // Si la forme a des styles de remplissage, c'est probablement un sol
                        tile.type = TilesData.TileType.ground;
                    }
                    else
                    {
                        // Sinon c'est un objet
                        tile.type = TilesData.TileType.objet;
                    }

                    // Ajouter la tuile à la collection appropriée selon son type
                    if (tile.type == TilesData.TileType.ground)
                    {
                        TilesData.Add_Ground(tile);
                    }
                    else
                    {
                        TilesData.Add_Object(tile);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Erreur lors du chargement d'une tuile : {ex.Message}");
            }
        }

        private void ProcessPlaceObject(PlaceObject2Tag placeTag)
        {
            try
            {
                // Déterminer la position de la cellule basée sur la matrice de transformation
                var matrix = placeTag.Matrix;

                // Déboggage pour examiner la structure de la matrice
                var properties = matrix.GetType().GetProperties();
                string debug = "Propriétés disponibles de Matrix:\n";
                foreach (var prop in properties)
                {
                    debug += $"{prop.Name}: {prop.GetValue(matrix)}\n";
                }
                System.Windows.Forms.MessageBox.Show(debug);

                // Pour le moment, utilisons des valeurs par défaut
                int cellX = 0;
                int cellY = 0;
                
                // Calculer l'ID de la cellule
                int cellId = cellX + (cellY * targetMap.Width);
                
                if (cellId < targetMap.Cells.Length)
                {
                    // Créer une nouvelle cellule si elle n'existe pas
                    if (targetMap.Cells[cellId] == null)
                    {
                        targetMap.Cells[cellId] = new CellsData();
                        targetMap.Cells[cellId].ID = cellId;
                    }

                    // Récupérer la référence de l'objet placé
                    int characterId = placeTag.CharacterId;
                    
                    // Déterminer la couche basée sur la profondeur du PlaceObject
                    int depth = placeTag.Depth;
                    
                    // Assigner l'objet à la bonne couche de la cellule
                    if (depth == 1) // Sol
                    {
                        targetMap.Cells[cellId].GFX1 = TilesData.Get_Grounds(characterId);
                    }
                    else if (depth == 2) // Objets couche 1
                    {
                        targetMap.Cells[cellId].GFX2 = TilesData.Get_Objects(characterId);
                    }
                    else if (depth == 3) // Objets couche 2
                    {
                        targetMap.Cells[cellId].GFX3 = TilesData.Get_Objects(characterId);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Erreur lors du placement d'un objet : {ex.Message}");
            }
        }
    }
} 