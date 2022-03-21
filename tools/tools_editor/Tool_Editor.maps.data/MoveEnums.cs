using System;

namespace Tool_Editor.maps.data
{
	public enum MoveEnums
	{
		NONE = -1,
		UNWALKABLE = 0,
		DOOR = 1,
		TRIGGER = 2,
		WALKABLE = 4,
		PADDOCK = 5,
		PATH = 7
	}
    public enum OutilsEnum
    {
        CellMove,
        CellWalkable,
        CellWalkableInRP,
        CellWalkableInFight,
        Paint,
        TriggerChangeMap,
        Selection,
        Unknow,
    }

    public enum TypeChangeMapEnum
    {
        Top,
        Bot,
        Left,
        Right,
        Unknow,

    }
    public enum LayerEnum
    {
        Ground = 0,
        Additional_Ground = 1,
        Decor = 2,
        Additional_Decor = 3,
        Unknow,
    }
}