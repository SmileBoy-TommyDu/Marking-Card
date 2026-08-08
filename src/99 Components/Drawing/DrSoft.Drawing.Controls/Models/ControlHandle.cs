using DrSoft.Drawing.Controls.DrawShapes;
using SkiaSharp;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;


namespace GraphicEditor.WPF;

/// <summary>
/// 控制点基类
/// </summary>
public abstract class ControlHandle
{
    protected DrawObject Shape { get; }
    public SKPoint Position { get; protected set; }
    public bool IsDragging { get; protected set; }
    protected SKPoint DragStart { get; set; }
    protected SKPoint ShapeCenterStart { get; set; }
    protected SKPoint RotationCenterStart { get; set; }
    protected (float w, float h, float scaleX, float scaleY) StartSize { get; set; }
    
    protected const float HitRadius = 8;
    
    public ControlHandle(DrawObject shape, SKPoint position)
    {
        Shape = shape;
        Position = position;
    }
    
    public virtual bool HitTest(SKPoint worldPoint)
    {
        float dx = worldPoint.X - Position.X;
        float dy = worldPoint.Y - Position.Y;
        return dx * dx + dy * dy <= HitRadius * HitRadius;
    }
    
    public virtual void StartDrag(SKPoint worldPoint)
    {
        IsDragging = true;
        DragStart = worldPoint;
        ShapeCenterStart = Shape.SharpCenter;
        RotationCenterStart = Shape.RotationCenterLocal;
        StartSize = (Shape.Width, Shape.Height, Shape.ScaleX, Shape.ScaleY);
    }
    
    public abstract void Drag(SKPoint worldPoint);
    
    public virtual void EndDrag()
    {
        IsDragging = false;
    }
    
    public abstract void Render(SKCanvas canvas);
    
    public abstract Cursor GetCursor();
}

/// <summary>
/// 缩放控制点
/// </summary>
public class ScaleHandle : ControlHandle
{
    private readonly string _positionName;
    public bool _isCorner { get; private set; }
    
    public ScaleHandle(DrawObject shape, string positionName, SKPoint position, bool isCorner)
        : base(shape, position)
    {
        _positionName = positionName;
        _isCorner = isCorner;
    }
    
    public override void Drag(SKPoint worldPoint)
    {
        //float dx = worldPoint.X - DragStart.X;
        //float dy = worldPoint.Y - DragStart.Y;
        
        // 转换到图形本地坐标系
        //var inverseMatrix = Shape.GetInverseMatrix();
        //var localDragStart = inverseMatrix.MapPoint(DragStart);
        //var localCurrent = inverseMatrix.MapPoint(worldPoint);
        
        //float localDx = localCurrent.X - localDragStart.X;
        //float localDy = localCurrent.Y - localDragStart.Y;
        
        // 根据位置名称处理缩放
        //switch (_positionName)
        //{
        //    case "topLeft":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X + localDx / 2, ShapeCenterStart.Y + localDy / 2);
        //        break;
        //    case "top":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X, ShapeCenterStart.Y + localDy / 2);
        //        break;
        //    case "topRight":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X + localDx / 2, ShapeCenterStart.Y + localDy / 2);
        //        break;
        //    case "right":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X + localDx / 2, ShapeCenterStart.Y);
        //        break;
        //    case "bottomRight":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X + localDx / 2, ShapeCenterStart.Y + localDy / 2);
        //        break;
        //    case "bottom":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X, ShapeCenterStart.Y + localDy / 2);
        //        break;
        //    case "bottomLeft":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X + localDx / 2, ShapeCenterStart.Y + localDy / 2);
        //        break;
        //    case "left":
        //        Shape.SharpCenter = new SKPoint(ShapeCenterStart.X + localDx / 2, ShapeCenterStart.Y);
        //        break;
        //}
        
        // 更新位置
        Position = Shape.GetTransformMatrix().MapPoint(GetHandleLocalPosition());
    }
    
    private SKPoint GetHandleLocalPosition()
    {
        return _positionName switch
        {
            "topLeft" => new SKPoint(-Shape.Width / 2, -Shape.Height / 2),
            "top" => new SKPoint(0, -Shape.Height / 2),
            "topRight" => new SKPoint(Shape.Width / 2, -Shape.Height / 2),
            "right" => new SKPoint(Shape.Width / 2, 0),
            "bottomRight" => new SKPoint(Shape.Width / 2, Shape.Height / 2),
            "bottom" => new SKPoint(0, Shape.Height / 2),
            "bottomLeft" => new SKPoint(-Shape.Width / 2, Shape.Height / 2),
            "left" => new SKPoint(-Shape.Width / 2, 0),
            _ => new SKPoint(0, 0)
        };
    }
    
    public override void Render(SKCanvas canvas)
    {
        // 角点是蓝色方块，边中点是绿色圆点
        using var paint = new SKPaint
        {
            Color = _isCorner ? SKColors.DodgerBlue : SKColors.LimeGreen,
            Style = SKPaintStyle.Fill
        };
        
        if (_isCorner)
        {
            canvas.DrawRect(Position.X - 6, Position.Y - 6, 12, 12, paint);
        }
        else
        {
            canvas.DrawCircle(Position, 6, paint);
        }
        
        // 边框
        using var strokePaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        
        if (_isCorner)
        {
            canvas.DrawRect(Position.X - 6, Position.Y - 6, 12, 12, strokePaint);
        }
        else
        {
            canvas.DrawCircle(Position, 6, strokePaint);
        }
    }
    
    public override Cursor GetCursor()
    {
        return _positionName switch
        {
            "topLeft" or "bottomRight" => Cursors.SizeNWSE,
            "topRight" or "bottomLeft" => Cursors.SizeNESW,
            "top" or "bottom" => Cursors.SizeNS,
            "left" or "right" => Cursors.SizeWE,
            _ => Cursors.Arrow
        };
    }
}

/// <summary>
/// 旋转控制点
/// </summary>
public class RotateHandle : ControlHandle
{
    private float _startRotation;
    private SKPoint _shapeCenter;
    
    public RotateHandle(DrawObject shape, SKPoint position) : base(shape, position)
    {
    }
    
    public override void StartDrag(SKPoint worldPoint)
    {
        base.StartDrag(worldPoint);
        _startRotation = Shape.Rotation;
        // 旋转中心世界坐标 = 图形中心 + 偏移量
        _shapeCenter = new SKPoint(
            Shape.SharpCenter.X + Shape.RotationCenterLocal.X,
            Shape.SharpCenter.Y + Shape.RotationCenterLocal.Y
        );
    }
    
    public override void Drag(SKPoint worldPoint)
    {
        // 计算旋转角度
        float dx = worldPoint.X - _shapeCenter.X;
        float dy = worldPoint.Y - _shapeCenter.Y;
        float angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
        
        // 加上90度（因为控制点在顶部）
        Shape.Rotation = angle + 90;
        
        // 更新控制点位置
        var matrix = Shape.GetTransformMatrix();
        Position = matrix.MapPoint(new SKPoint(0, -Shape.Height / 2 - 25));
    }
    
    public override void Render(SKCanvas canvas)
    {
        // 绘制连接线
        using var linePaint = new SKPaint
        {
            Color = SKColors.Purple,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        canvas.DrawLine(Shape.SharpCenter.X, Shape.SharpCenter.Y, Position.X, Position.Y, linePaint);
        
        // 绘制旋转手柄（紫色圆圈）
        using var paint = new SKPaint
        {
            Color = SKColors.Purple,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(Position, 7, paint);
        
        using var strokePaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        canvas.DrawCircle(Position, 7, strokePaint);
    }
    
    public override Cursor GetCursor() => Cursors.SizeAll;
}

/// <summary>
/// 旋转中心控制点
/// </summary>
public class RotationCenterHandle : ControlHandle
{
    public RotationCenterHandle(DrawObject shape, SKPoint position) : base(shape, position)
    {
    }
    
    public override void Drag(SKPoint worldPoint)
    {
        // 更新旋转中心偏移（相对于图形中心）
        Shape.RotationCenterLocal = new SKPoint(
            worldPoint.X - Shape.SharpCenter.X,
            worldPoint.Y - Shape.SharpCenter.Y
        );
        
        // 更新位置显示（旋转中心是世界坐标）
        Position = worldPoint;
    }
    
    public override void Render(SKCanvas canvas)
    {
        // 绘制橙色十字
        using var paint = new SKPaint
        {
            Color = SKColors.Orange,
            Style = SKPaintStyle.Fill
        };
        
        // 十字线
        paint.StrokeWidth = 2;
        paint.Style = SKPaintStyle.Stroke;
        
        canvas.DrawLine(Position.X - 8, Position.Y, Position.X + 8, Position.Y, paint);
        canvas.DrawLine(Position.X, Position.Y - 8, Position.X, Position.Y + 8, paint);
        
        // 中心点
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawCircle(Position, 3, paint);
    }
    
    public override Cursor GetCursor() => Cursors.SizeAll;
}