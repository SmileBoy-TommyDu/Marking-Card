namespace DrSoft.Drawing.Model
{
    /// <summary>
    /// 轻量级 IShapeData 实现，用于"非画布来源"的图形数据构建（如校准图形）。
    /// 无渲染依赖，无 SkiaSharp，可安全在打标卡 UI 层使用。
    /// </summary>
    public class SimpleLineShapeData : ILineShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.Line;

        // 几何
        public float X      { get; init; }
        public float Y      { get; init; }
        public float Width  { get; init; }
        public float Height { get; init; }
        public float CenterX { get; init; }
        public float CenterY { get; init; }

        // 变换
        public float Rotation { get; init; }
        public float ScaleX   { get; init; } = 1f;
        public float ScaleY   { get; init; } = 1f;
        public float SkewX    { get; init; }
        public float SkewY    { get; init; }

        // 外观
        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;
        public LineStyle LineStyle { get; init; } = LineStyle.Solid;

        // 加工语义
        public bool IsClockwise { get; init; }
        public bool IsVisible   { get; init; } = true;
        public bool IsLocked    { get; init; }

        // 打标数据
        public IReadOnlyList<(float X, float Y)> OutlinePoints           { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints  { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }
        public int SelfIntersectionSkipCount { get; init; }

        // 子图形
        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();
    }

    /// <summary>
    /// 轻量级圆/椭圆数据实现，用于校准等非画布场景。
    /// </summary>
    /*public class SimpleCircleShapeData : ICircleShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.Circle;

        public float X      { get; init; }
        public float Y      { get; init; }
        public float Width  { get; init; }
        public float Height { get; init; }
        public float CenterX { get; init; }
        public float CenterY { get; init; }

        public float Rotation { get; init; }
        public float ScaleX   { get; init; } = 1f;
        public float ScaleY   { get; init; } = 1f;
        public float SkewX    { get; init; }
        public float SkewY    { get; init; }

        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;

        public bool IsClockwise { get; init; }
        public bool IsVisible   { get; init; } = true;
        public bool IsLocked    { get; init; }

        public IReadOnlyList<(float X, float Y)> OutlinePoints           { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints  { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }

        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();

        // ICircleShapeData
        public float RadiusX   { get; init; }
        public float RadiusY   { get; init; }
        public bool  IsEllipse { get; init; }
    }*/

    /// <summary>
    /// 轻量级 ILayerData 实现，用于"非画布来源"的图层数据构建（如校准图形）。
    /// 无渲染依赖，可安全在打标卡 UI 层使用。
    /// </summary>
    /*public class SimpleLayerData : ILayerData
    {
        public int    UId       { get; init; }
        public string Name      { get; init; } = string.Empty;
        public bool   IsVisible { get; init; } = true;
        public bool   IsLocked  { get; init; }
        public string Color     { get; init; } = "#000000";
        public IReadOnlyList<IShapeData> Shapes { get; init; } = Array.Empty<IShapeData>();
    }*/

    /// <summary>轻量级圆弧数据</summary>
    /*public class SimpleArcShapeData : IArcShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.Arc;
        public float X { get; init; } public float Y { get; init; }
        public float Width { get; init; } public float Height { get; init; }
        public float CenterX { get; init; } public float CenterY { get; init; }
        public float Rotation { get; init; }
        public float ScaleX { get; init; } = 1f; public float ScaleY { get; init; } = 1f;
        public float SkewX { get; init; } public float SkewY { get; init; }
        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;

        public bool IsClockwise { get; init; } public bool IsVisible { get; init; } = true; public bool IsLocked { get; init; }
        public IReadOnlyList<(float X, float Y)> OutlinePoints { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }
        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();
        public float Radius { get; init; }
        public float StartAngle { get; init; }
        public float SweepAngle { get; init; }
    }*/

    /// <summary>轻量级折线数据</summary>
    /*public class SimplePolyLineShapeData : IPolyLineShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.PolyLine;
        public float X { get; init; } public float Y { get; init; }
        public float Width { get; init; } public float Height { get; init; }
        public float CenterX { get; init; } public float CenterY { get; init; }
        public float Rotation { get; init; }
        public float ScaleX { get; init; } = 1f; public float ScaleY { get; init; } = 1f;
        public float SkewX { get; init; } public float SkewY { get; init; }
        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;

        public bool IsClockwise { get; init; } public bool IsVisible { get; init; } = true; public bool IsLocked { get; init; }
        public IReadOnlyList<(float X, float Y)> OutlinePoints { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }
        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();
        public IReadOnlyList<(float X, float Y)> Vertices { get; init; } = Array.Empty<(float, float)>();

        public bool IsClosed {  get; init; }
    }*/

    /// <summary>轻量级矩形数据</summary>
    /*public class SimpleRectangleShapeData : IRectangleShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.Rectangle;
        public float X { get; init; } public float Y { get; init; }
        public float Width { get; init; } public float Height { get; init; }
        public float CenterX { get; init; } public float CenterY { get; init; }
        public float Rotation { get; init; }
        public float ScaleX { get; init; } = 1f; public float ScaleY { get; init; } = 1f;
        public float SkewX { get; init; } public float SkewY { get; init; }
        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;

        public bool IsClockwise { get; init; } public bool IsVisible { get; init; } = true; public bool IsLocked { get; init; }
        public IReadOnlyList<(float X, float Y)> OutlinePoints { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }
        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();
        public float CornerRadiusTopLeft { get; init; }
        public float CornerRadiusTopRight { get; init; }
        public float CornerRadiusBottomRight { get; init; }
        public float CornerRadiusBottomLeft { get; init; }

        public float ChamferTopLeft { get; init; }

        public float ChamferTopRight { get; init; }

        public float ChamferBottomRight { get; init; }

        public float ChamferBottomLeft { get; init; }
    }*/

    /// <summary>轻量级文字数据</summary>
   /* public class SimpleTextShapeData : ITextShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.Text;
        public float X { get; init; } public float Y { get; init; }
        public float Width { get; init; } public float Height { get; init; }
        public float CenterX { get; init; } public float CenterY { get; init; }
        public float Rotation { get; init; }
        public float ScaleX { get; init; } = 1f; public float ScaleY { get; init; } = 1f;
        public float SkewX { get; init; } public float SkewY { get; init; }
        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;

        public bool IsClockwise { get; init; } public bool IsVisible { get; init; } = true; public bool IsLocked { get; init; }
        public IReadOnlyList<(float X, float Y)> OutlinePoints { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }
        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();
        public string Text { get; init; } = string.Empty;
        public string FontFamily { get; init; } = string.Empty;
        public float  FontSize { get; init; }
        public bool   IsBold { get; init; }
        public bool   IsItalic { get; init; }
        public float  LineHeight { get; init; }
        public float  CharacterSpacing { get; init; }
    }
*/
    /// <summary>轻量级点数据</summary>
    /*public class SimpleDotShapeData : IDotShapeData
    {
        public int       UId      { get; init; }
        public string    Name     { get; init; } = string.Empty;
        public int       LayerId  { get; init; }
        public ShapeType Type     => ShapeType.Point;
        public float X { get; init; } public float Y { get; init; }
        public float Width { get; init; } public float Height { get; init; }
        public float CenterX { get; init; } public float CenterY { get; init; }
        public float Rotation { get; init; }
        public float ScaleX { get; init; } = 1f; public float ScaleY { get; init; } = 1f;
        public float SkewX { get; init; } public float SkewY { get; init; }
        public DrawingColor? OutlineColor { get; init; }
        public OutlineStyle OutlineStyle { get; init; } = OutlineStyle.Solid;

        public bool IsClockwise { get; init; } public bool IsVisible { get; init; } = true; public bool IsLocked { get; init; }
        public IReadOnlyList<(float X, float Y)> OutlinePoints { get; init; } = Array.Empty<(float, float)>();
        public IReadOnlyList<(float X, float Y)> IntersectionSkipPoints { get; init; } = Array.Empty<(float, float)>();
        public float IntersectionSkipRadius { get; init; }
        public IReadOnlyList<IShapeData> ChildShapes => Array.Empty<IShapeData>();
    }*/
}
