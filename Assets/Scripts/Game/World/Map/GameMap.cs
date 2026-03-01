using UnityEngine;

namespace Game.World.Map
{
    public class GameMap : MonoBehaviour
    {
        public static GameMap Main { get; private set; }

        public MapRenderer Renderer;
        public MapProvider Provider;
        public MapPicker Picker;
        public MapFogOfWar FogOfWar;
        public MapAdjacentHighlighter Highlighter;
        public MapConnections Connections;
        public MapBorderRenderer BorderRenderer;

        private void Awake()
        {
            Main = this;
            Renderer = GetComponent<MapRenderer>();
            Provider = GetComponent<MapProvider>();
            Picker = GetComponent<MapPicker>();
            FogOfWar = GetComponent<MapFogOfWar>();
            Highlighter = GetComponent<MapAdjacentHighlighter>();
            Connections = GetComponent<MapConnections>();
            BorderRenderer = GetComponent<MapBorderRenderer>();
        }
    }
}
