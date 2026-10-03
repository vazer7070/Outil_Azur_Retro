using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;

// Retains the entry point used by the existing map editor.
public static class SwfReader
{
    public static Map UnPackerSwf(string path) { return MapSwfSerializer.Load(path); }
}
