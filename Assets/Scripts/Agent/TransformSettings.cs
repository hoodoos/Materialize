using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MaterializeAgent
{
    public sealed class AlignmentSettings
    {
        public Vector2 pointTL, pointTR, pointBL, pointBR;
        public float LensDistort, PerspectiveX, PerspectiveY;
        public AlignmentSettings(JToken json)
        {
            pointTL = Point(json["pointTL"]); pointTR = Point(json["pointTR"]);
            pointBL = Point(json["pointBL"]); pointBR = Point(json["pointBR"]);
            LensDistort = (float)json["LensDistort"]; PerspectiveX = (float)json["PerspectiveX"]; PerspectiveY = (float)json["PerspectiveY"];
        }
        static Vector2 Point(JToken value) => new Vector2((float)value[0], (float)value[1]);
    }
    public sealed class TilingSettings
    {
        public int width, height;
        public string technique;
        public float Falloff, OverlapX, OverlapY, SplatRotation, SplatRotationRandom, SplatScale, SplatWobble, SplatRandomize;
        public TilingSettings(JToken json)
        {
            width = (int)json["width"]; height = (int)json["height"]; technique = (string)json["technique"];
            Falloff = (float)json["Falloff"]; OverlapX = (float)json["OverlapX"]; OverlapY = (float)json["OverlapY"];
            SplatRotation = (float)json["SplatRotation"]; SplatRotationRandom = (float)json["SplatRotationRandom"];
            SplatScale = (float)json["SplatScale"]; SplatWobble = (float)json["SplatWobble"]; SplatRandomize = (float)json["SplatRandomize"];
        }
    }
}
