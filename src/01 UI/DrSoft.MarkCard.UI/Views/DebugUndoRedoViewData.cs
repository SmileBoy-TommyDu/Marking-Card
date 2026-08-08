using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace DrSoft.MarkCard.UI.Views;

internal sealed class DebugUndoRedoEntryViewData
{
    public string Operation { get; init; } = string.Empty;
    public string Elements { get; init; } = string.Empty;
    public string CommandType { get; init; } = string.Empty;
    public string DisplayText => $"{Operation} | {Elements} | {CommandType}";
}

internal sealed class DebugUndoRedoSectionViewData
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public ObservableCollection<DebugUndoRedoEntryViewData> Entries { get; } = new();
}

internal sealed class DebugUndoRedoViewData
{
    public string CanvasLabel { get; init; } = string.Empty;
    public string CountsLabel { get; init; } = string.Empty;
    public ObservableCollection<DebugUndoRedoSectionViewData> Sections { get; } = new();

    public static DebugUndoRedoViewData Build(DrawingCanvas? canvas)
    {
        if (canvas == null)
        {
            return new DebugUndoRedoViewData
            {
                CanvasLabel = "无活动画布",
                CountsLabel = "Undo: 0    Redo: 0"
            };
        }

        var canvasLabel = string.IsNullOrWhiteSpace(canvas.Name)
            ? $"Canvas#{canvas.Id}"
            : canvas.Name;

        var stateSnapshot = canvas.CommandHistory.CaptureStateSnapshot();
        var commandSnapshot = canvas.CommandHistory.CaptureCommandSnapshot();

        var result = new DebugUndoRedoViewData
        {
            CanvasLabel = $"画布: {canvasLabel}",
            CountsLabel = $"Undo: {stateSnapshot.UndoCount}    Redo: {stateSnapshot.RedoCount}"
        };

        var undoSection = BuildSection("Undo 栈", commandSnapshot.UndoCommands);
        var redoSection = BuildSection("Redo 栈", commandSnapshot.RedoCommands);

        result.Sections.Add(undoSection);
        result.Sections.Add(redoSection);

        return result;
    }

    private static DebugUndoRedoSectionViewData BuildSection(
        string title,
        IReadOnlyList<IDrawingCommand> commands)
    {
        string summary;
        if (commands.Count == 0)
        {
            summary = "<empty>";
        }
        else
        {
            summary = $"{commands.Count} item(s)";
        }

        var section = new DebugUndoRedoSectionViewData
        {
            Title = title,
            Summary = summary
        };

        foreach (var command in commands)
        {
            var entry = new DebugUndoRedoEntryViewData
            {
                Operation = ResolveOperation(command),
                Elements = ResolveElements(command),
                CommandType = command.GetType().Name
            };
            section.Entries.Add(entry);
        }

        return section;
    }

    private static string ResolveOperation(IDrawingCommand command)
    {
        var mappedOperation = TryMapOperation(command);
        if (!string.IsNullOrWhiteSpace(mappedOperation))
        {
            return mappedOperation;
        }

        var description = command.Description;
        var hasReadableDescription = !string.IsNullOrWhiteSpace(description)
            && description.Any(ch => ch < 0xFFFD && ch != '�');
        if (hasReadableDescription)
        {
            return description;
        }

        return command.GetType().Name;
    }

    private static string? TryMapOperation(IDrawingCommand command)
    {
        var commandTypeName = command.GetType().Name;
        var mappedOperation = commandTypeName switch
        {
            nameof(CompositeCommand) => "复合操作",
            "CommandAdd" => "新增图形",
            "CommandRemove" => "删除图形",
            "CommandTransform" => "变换",
            "CommandEdit" => "编辑属性",
            "CommandMoveNode" => "移动节点",
            "CommandFontSettings" => "修改字体",
            "CommandMirror" => "镜像",
            "CommandJumpPoint" => "设置跳点",
            "CommandAddLayer" => "新增图层",
            "CommandRemoveLayer" => "删除图层",
            "CommandLock" => "锁定切换",
            "CommandRefill" => "重建填充",
            "CommandContainerScale" => "调整大小",
            _ => null
        };

        return mappedOperation;
    }

    private static string ResolveElements(IDrawingCommand command)
    {
        var shapes = new List<IShape>();
        CollectShapes(command, shapes);

        if (shapes.Count == 0)
        {
            return "-";
        }

        var labels = shapes
            .Select(FormatShape)
            .Distinct()
            .ToList();

        var text = string.Join(", ", labels);
        return text;
    }

    private static void CollectShapes(IDrawingCommand command, List<IShape> shapes)
    {
        if (command is CompositeCommand compositeCommand)
        {
            foreach (var childCommand in compositeCommand.Commands)
            {
                CollectShapes(childCommand, shapes);
            }

            return;
        }

        var fields = command.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            var value = field.GetValue(command);
            if (value == null)
            {
                continue;
            }

            if (value is IShape shape)
            {
                shapes.Add(shape);
                continue;
            }

            if (value is IEnumerable<IShape> shapeEnumerable)
            {
                foreach (var item in shapeEnumerable)
                {
                    if (item != null)
                    {
                        shapes.Add(item);
                    }
                }

                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is IShape nestedShape)
                    {
                        shapes.Add(nestedShape);
                    }
                }
            }
        }
    }

    private static string FormatShape(IShape shape)
    {
        var typeName = shape.Type.ToString();
        if (string.IsNullOrWhiteSpace(shape.Name))
        {
            return typeName;
        }

        var label = $"{typeName}({shape.Name})";
        return label;
    }
}
