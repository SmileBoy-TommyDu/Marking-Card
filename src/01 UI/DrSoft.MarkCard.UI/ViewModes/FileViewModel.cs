using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DrSoft.MarkCard.Impl.Storage;


namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class FileViewModel : ObservableObject
    {
        private readonly ILogger<FileViewModel> _logger;
        private readonly IDrawingService _drawingService;
        public ICommand NewFileCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand CloseFileCommand { get; }
        public ICommand SaveFileCommand { get; }
        public ICommand SaveAsFileCommand { get; }
        public ICommand ImportDxfCommand { get; }
        public ICommand ExportDxfCommand { get; }


        private readonly IEventBus _eventBus;

        private readonly IMarkingParam? _markParam;

        // 将字典初始化移到字段声明处
        private readonly Dictionary<int, string> _filePaths = new Dictionary<int, string>();

        // 将 CurrentFilePath 的初始化移到字段声明处
        [ObservableProperty] private string _currentFilePath = string.Empty;

        // 将 _lastDirectory 的初始化移到字段声明处
        private string _lastDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>
        /// 文件ID
        /// </summary>
        private int _fileId = 0;

        private string currentDxfFilePath = string.Empty;

        private bool _noCanvas = false;

        private CanvasSnapshotDto? lastCanvasData;

        public FileViewModel(ILogger<FileViewModel> logger, IDrawingService drawingService)
        {
            _markParam = App.GetService<IMarkingParam>();
            _logger = logger;
            _drawingService = drawingService;
            _eventBus = EventBus.Instance;
            NewFileCommand = new RelayCommand(OnNewFile);
            OpenFileCommand = new RelayCommand(OnOpenFile);
            CloseFileCommand = new RelayCommand(OnCloseFile);
            SaveFileCommand = new RelayCommand(OnSaveFile);
            SaveAsFileCommand = new RelayCommand(OnSaveAsFile);
            ImportDxfCommand = new AsyncRelayCommand(OnImportDxf);
            ExportDxfCommand = new AsyncRelayCommand(OnExportDxf);


            _eventBus.Subscribe<CanvasChangedEvent>(data =>
            {
                _fileId = data.CanvasId == null ? 0 : data.CanvasId.Value;
                RuntimeContext.ActiveCanvasId = _fileId;
                switch (data.ChangeType)
                {
                    case CanvasChangeType.Created:
                        _noCanvas = false;
              
                        AddOrUpdateFilePath(data.CanvasId, data.CanvasName);
                        break;
                    case CanvasChangeType.Renamed:
                        AddOrUpdateFilePath(data.CanvasId, data.CanvasName);
                        break;
                    case CanvasChangeType.BeforeRemove:
                        PopUpForClose();
                        break;
                    case CanvasChangeType.Removed:
                        // 清理文件路径映射
                        if (_filePaths.ContainsKey(_fileId))
                        {
                            _filePaths.Remove(_fileId);
                        }
                        break;
                    case CanvasChangeType.NoCanvas:
                        _noCanvas = true;
                        _filePaths.Clear();
                        break;
                    case CanvasChangeType.Switched:
                        AddOrUpdateFilePath(data.CanvasId, data.CanvasName);
                        break;
                    case CanvasChangeType.SelectChanged:
                       // AddOrUpdateFilePath(data.CanvasId, data.CanvasName);
                        break;
                    default:
                        break;
                }
            });
        }


        /// <summary>
        /// 新建文件逻辑
        /// </summary>
        public void OnNewFile()
        {
            try
            {
                _drawingService.CanvasService.AddCanvas();
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"新建文件异常,{ex.Message}", ToastType.Error));
            }
        }

        /// <summary>
        /// 打开文件逻辑
        /// </summary>
        public void OnOpenFile()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    DefaultExt = ".drw",
                    Filter = "DRW 文件 (*.drw)|*.drw|所有文件 (*.*)|*.*"
                };

                bool? result = dlg.ShowDialog();
                if (result == true)
                {
                    _lastDirectory = Path.GetDirectoryName(dlg.FileName) ?? _lastDirectory;
                    if (!_filePaths.ContainsValue(dlg.FileName))
                    {
                        Open(dlg.FileName);
                    }
                    else
                    {
                        EventBus.Instance.Publish(new ToastMessageEvent($"该文件已打开，请重新选择", ToastType.Error));
                    }
                }
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"打开文件异常,{ex.Message}", ToastType.Error));
            }
        }


        /// <summary>
        /// 关闭文件逻辑 (包含核心修复)
        /// </summary>
        public void OnCloseFile()
        {
            try
            {
                if (_noCanvas) return;

                PopUpForClose();
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"关闭文件异常,{ex.Message}", ToastType.Error));
            }
        }


        /// <summary>
        /// 窗口关闭时调用，返回是否可以关闭
        /// </summary>
        /// <returns>返回 true 表示可以关闭窗口，false 表示取消关闭</returns>
        public bool CanCloseWindow()
        {
            try
            {
                if (_noCanvas) return true;
                // 如果有未保存的文件，弹窗询问
                bool result = PopUpForCloseAll();

                return result && _filePaths.Count == 0;
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"窗口关闭检查异常,{ex.Message}", ToastType.Error));
                return true; // 发生异常时允许关闭
            }
        }

        /// <summary>
        /// 弹窗处理及保存逻辑
        /// 修复点：修复了逻辑穿透漏洞（原代码无论点取消还是是，最后都会执行关闭）
        /// </summary>
        /// <returns>返回 true 表示可以关闭，false 表示取消关闭</returns>
        private bool PopUpForClose()
        {
            try
            {
                if (IsModified())
                {
                    var result = MessageBox.Show(
                        "当前文件尚未保存，是否保存更改？",
                        "确认关闭",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    switch (result)
                    {
                        case MessageBoxResult.Cancel:
                            // 用户取消关闭操作
                            return false;

                        case MessageBoxResult.No:
                            // 用户选择不保存，直接关闭
                            break;

                        case MessageBoxResult.Yes:
                            // 用户选择保存
                            if (!CurrentFilePath.Contains(':'))
                            {
                                // 新建文件首次保存
                                var dlg = new SaveFileDialog
                                {
                                    FileName = Path.GetFileName(CurrentFilePath),
                                    DefaultExt = ".drw",
                                    Filter = "DRW 文件 (*.drw)|*.drw|所有文件 (*.*)|*.*",
                                    AddExtension = true
                                };

                                if (!string.IsNullOrWhiteSpace(_lastDirectory) && Directory.Exists(_lastDirectory))
                                    dlg.InitialDirectory = _lastDirectory;

                                bool? dialogResult = dlg.ShowDialog();
                                if (dialogResult == true)
                                {
                                    _lastDirectory = Path.GetDirectoryName(dlg.FileName) ?? _lastDirectory;
                                    AddOrUpdateFilePath(_fileId, dlg.FileName); // 更新字典映射
                                }
                                else
                                {
                                    return false; // 用户在保存对话框取消，不执行关闭
                                }
                            }

                            // 检查文件是否已被其他画布打开
                            if (IsFileOpenedByOtherCanvas(CurrentFilePath, _fileId))
                            {
                                EventBus.Instance.Publish(new ToastMessageEvent($"该文件已打开，请重新选择", ToastType.Error));
                                return false;
                            }

                            var canvasSnapshot = GetCanvasSnapshot();
                            if (canvasSnapshot == null)
                            {
                                _logger.LogInformation("画布返回数据为空");
                                return false; // 保存失败，不执行关闭
                            }

                            Save(canvasSnapshot, CurrentFilePath);
                            break;

                        default:
                            return false;
                    }
                }

                // 执行关闭逻辑（Cancel/默认 已提前 return false，只有 Yes 保存成功或 No 不保存才走到这里）
                _drawingService.CanvasService.Close(_fileId);
                return true;
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"关闭文件弹窗时发生异常: {ex.Message}", ToastType.Error));
                return false;
            }
        }

        private CanvasSnapshotDto? GetCanvasSnapshot()
        {
            return _drawingService.CanvasService.GetActiveSnapshot();
        }

        private bool PopUpForCloseAll()
        {
            try
            {
                var result = MessageBox.Show(
                    "有文件尚未保存，是否要保存全部文件？",
                    "确认关闭",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Cancel:
                        return false;

                    case MessageBoxResult.No:
                        // 用户选择不保存，直接关闭全部
                        break;

                    case MessageBoxResult.Yes:
                        // 遍历所有打开的文件，逐个保存
                        // 注意：遍历字典副本以避免迭代时修改集合
                        var snapshot = new Dictionary<int, string>(_filePaths);
                        foreach (var kvp in snapshot)
                        {
                            int canvasId = kvp.Key;
                            string filePath = kvp.Value;

                            // 切换到目标画布以获取正确的快照
                            _drawingService.CanvasService.SwitchCanvas(canvasId);

                            // 检查是否首次保存（无盘符）
                            if (!filePath.Contains(':'))
                            {
                                var dlg = new SaveFileDialog
                                {
                                    FileName = Path.GetFileName(filePath),
                                    DefaultExt = ".drw",
                                    Filter = "DRW 文件 (*.drw)|*.drw|所有文件 (*.*)|*.*",
                                    AddExtension = true
                                };

                                if (!string.IsNullOrWhiteSpace(_lastDirectory) && Directory.Exists(_lastDirectory))
                                    dlg.InitialDirectory = _lastDirectory;

                                bool? dialogResult = dlg.ShowDialog();
                                if (dialogResult == true)
                                {
                                    _lastDirectory = Path.GetDirectoryName(dlg.FileName) ?? _lastDirectory;
                                    filePath = dlg.FileName;
                                    _filePaths[canvasId] = filePath;
                                }
                                else
                                {
                                    return false; // 用户取消，中止关闭
                                }
                            }

                            // 检查文件是否被其他画布打开
                            if (IsFileOpenedByOtherCanvas(filePath, canvasId))
                            {
                                EventBus.Instance.Publish(new ToastMessageEvent($"该文件已打开，请重新选择", ToastType.Error));
                                return false;
                            }

                            // 获取当前活动画布（已切换到 canvasId）的快照
                            var canvasSnapshot = GetCanvasSnapshot();
                            if (canvasSnapshot == null)
                            {
                                _logger.LogInformation("画布 {CanvasId} 返回数据为空", canvasId);
                                return false;
                            }

                            Save(canvasSnapshot, filePath);
                        }
                        break;

                    default:
                        return false;
                }

                // 关闭所有画布（只在保存成功或选择不保存后执行一次）
                var closeIds = new List<int>(_filePaths.Keys);
                foreach (var id in closeIds)
                {
                    _drawingService.CanvasService.Close(id);
                }
                _filePaths.Clear();

                return true;
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"关闭文件弹窗时发生异常: {ex.Message}", ToastType.Error));
                return false;
            }
        }

        /// <summary>
        /// 保存文件逻辑
        /// 修复点：补充了首次保存时的 ID-Path 映射更新
        /// </summary>
        public void OnSaveFile()
        {
            try
            {
                if (_noCanvas) return;

                // 标记是否为首次保存（路径由空变有）
                bool isFirstSave = !CurrentFilePath.Contains(':');

                // 如果当前没有文件路径（即新建文件第一次保存），需要弹出对话框
                if (isFirstSave)
                {
                    if (!ShowSaveFileDialog(out string filePath, out string fileName))
                        return;
                    if (IsFileOpenedByOtherCanvas(filePath, _fileId))
                    {
                        MessageBox.Show("该文件已打开，请重新选择", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    // 修复：如果是首次保存，需要更新 _filePaths 中的记录
                    AddOrUpdateFilePath(_fileId, filePath);
                }

                SaveCurrentCanvas(_fileId, CurrentFilePath);
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"保存文件异常: {ex.Message}", ToastType.Error));
            }
        }


        /// <summary>
        /// 另存为文件逻辑
        /// </summary>
        private void OnSaveAsFile()
        {
            try
            {
                if (_noCanvas) return;

                if (!ShowSaveFileDialog(out string filePath, out string fileName))
                    return;
                if (IsFileOpenedByOtherCanvas(filePath, _fileId))
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"该文件已打开，请重新选择", ToastType.Error));
                    return;
                }

                _lastDirectory = Path.GetDirectoryName(filePath) ?? _lastDirectory;
                var canvasSnapshot = GetCanvasSnapshot();

                if (canvasSnapshot != null)
                {
                    // 修复：更新字典映射
                    AddOrUpdateFilePath(_fileId, filePath);
                    Save(canvasSnapshot, filePath);
                }
                else
                {
                    _logger.LogInformation("画布返回数据为空");
                }
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"另存为文件异常: {ex.Message}", ToastType.Error));
            }
        }

        private bool ShowSaveFileDialog(out string filePath, out string fileName)
        {
            var dlg = new SaveFileDialog
            {
                FileName = Path.GetFileName(CurrentFilePath),
                DefaultExt = ".drw",
                Filter = "DRW 文件 (*.drw)|*.drw|所有文件 (*.*)|*.*",
                AddExtension = true
            };

            if (!string.IsNullOrWhiteSpace(_lastDirectory) && Directory.Exists(_lastDirectory))
                dlg.InitialDirectory = _lastDirectory;

            bool? result = dlg.ShowDialog();
            if (result == true)
            {
                _lastDirectory = Path.GetDirectoryName(dlg.FileName) ?? _lastDirectory;
                filePath = dlg.FileName;
                fileName = dlg.SafeFileName;
                return true;
            }

            filePath = string.Empty;
            fileName = string.Empty;
            return false;
        }


        public async Task OnImportDxf()
        {
            try
            {
              
                // 1. 弹出打开文件对话框
                var dlg = new OpenFileDialog
                {
                    DefaultExt = ".dxf",
                    // 仅允许选择 DXF 文件
                    Filter = "DXF 文件 (*.dxf)|*.dxf",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() != true)
                {
                    return;
                }

                // 强制检查扩展名为 .dxf
                if (Path.GetExtension(dlg.FileName)?.ToLowerInvariant() != ".dxf")
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"只能导入 .dxf 文件，请重新选择", ToastType.Error));
                    return;
                }

                // 2. 导入
                var success = await _drawingService.CanvasService.ImportDxfAsync(dlg.FileName);
                if (success)
                {
                    var data = GetCanvasSnapshot();
                    if (data != null)
                    {
                        currentDxfFilePath= dlg.FileName;
                        //AddOrUpdateFilePath(data.Id, dlg.FileName);
                        EventBus.Instance.Publish(new ToastMessageEvent($"成功导入 DXF 文件:\n{dlg.FileName}", ToastType.Info));
                    }
                }
                else
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"导入 DXF 文件失败，请查看日志获取详细信息", ToastType.Error));
                }
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"导入 DXF 文件时发生异常: {ex.Message}", ToastType.Error));
            }
        }

        public async Task OnExportDxf()
        {
            try
            {
                if (_noCanvas) return;
                // 3. 弹出保存文件对话框
                var dlg = new SaveFileDialog
                {
                    DefaultExt = ".dxf",
                    // 仅允许保存为 DXF
                    Filter = "DXF 文件 (*.dxf)|*.dxf",
                    AddExtension = true,
                    FileName = string.IsNullOrEmpty(CurrentFilePath)
                        ? "export.dxf"
                        : Path.GetFileNameWithoutExtension(CurrentFilePath)
                };

                if (dlg.ShowDialog() != true)
                {
                    return;
                }

                // 4. 调用 DXFHelper 导出
                // 强制使用 .dxf 扩展名
                if (Path.GetExtension(dlg.FileName)?.ToLowerInvariant() != ".dxf")
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"设置导出文件的后缀名异常", ToastType.Error));
                    return;
                }

                // 4. 调用 DXFHelper 导出
                bool success = await _drawingService.CanvasService.ExportDxfAsync(dlg.FileName);

                if (success)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"成功导出 DXF 文件到:\n{dlg.FileName}", ToastType.Info));
                }
                else
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"导出 DXF 文件失败，请查看日志获取详细信息", ToastType.Error));
                }
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"导出 DXF 文件失败，请查看日志获取详细信息", ToastType.Error));
            }
        }

        public void Open(string filePath)
        {
            try
            {
                if (!Path.GetExtension(filePath).Equals(".drw", StringComparison.OrdinalIgnoreCase))
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"当前仅支持打开 .drw 文件", ToastType.Error));
                    return;
                }

                var document = _drawingService.CanvasService.LoadFile(filePath);
                if (document?.CanvasSnapshot == null)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"文件加载失败，请确认文件格式是否正确", ToastType.Error));
                    return;
                }

                var data = _drawingService.CanvasService.Open(document.CanvasSnapshot);
                if (data.HasValue)
                {
                    TryRestoreStoredParameters(data.Value, document);
                    _fileId = data.Value;
                    AddOrUpdateFilePath(_fileId, filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取文件失败");
                EventBus.Instance.Publish(new ToastMessageEvent($"读取文件失败，请确认文件格式是否正确", ToastType.Error));
            }
        }


        private void SaveCurrentCanvas(int canvasId, string filePath)
        {
            lastCanvasData = GetCanvasSnapshot();
            var pairs = _markParam?.GetParameters(canvasId);
            var extensionPayloads = pairs?.Count > 0
                ? MarkingParamHelper.SerializeParams(pairs)
                : null;
            _drawingService.CanvasService.SaveFile(filePath, null, extensionPayloads);
        }

        private void Save(CanvasSnapshotDto canvasSnapshot, string filePath, bool IsDefaultType = true)
        {
            try
            {
                if (canvasSnapshot == null) return;
                if (!IsDefaultType) return;
                SaveCurrentCanvas(canvasSnapshot.Id, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存文件失败");
            }
        }


        private bool IsModified()
        {
            try
            {
                CanvasSnapshotDto? canvasSnapshotNew  = GetCanvasSnapshot();
                if (canvasSnapshotNew != null)
                {
                    if(canvasSnapshotNew.Layers.Count==1&& canvasSnapshotNew.Layers[0].Shapes.Count==0)
                    {
                        return false;
                    }
                    if(lastCanvasData != null)
                    {
                        if (lastCanvasData.Layers.SequenceEqual(canvasSnapshotNew.Layers))
                        {
                            return false;
                        }
                    }
                 
                    return true;
                }
                else
                {
                    _logger.LogInformation("画布返回数据为空");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "比对文件失败");
                return false;
            }
        }


        /// <summary>
        /// 添加或更新文件路径映射
        /// </summary>
        /// <param name="fileId">文件ID</param>
        /// <param name="filePath">文件路径</param>
        private void AddOrUpdateFilePath(int? fileId, string filePath)
        {
            if (!fileId.HasValue) return;

            int id = fileId.Value;

            // 处理 DXF 导入路径暂存
            if (!string.IsNullOrEmpty(currentDxfFilePath) && !_filePaths.ContainsValue(currentDxfFilePath))
            {
                _filePaths[id] = currentDxfFilePath;
                currentDxfFilePath = string.Empty;
            }

            if (_filePaths.TryGetValue(id, out string? existingPath))
            {
                // 已有记录：文件名变化或首次获得完整路径时更新
                if (Path.GetFileNameWithoutExtension(existingPath) != Path.GetFileNameWithoutExtension(filePath))
                {
                    _filePaths[id] = filePath;
                }
                else if (!existingPath.Contains(':') && filePath.Contains(':'))
                {
                    // 从临时名称升级为完整磁盘路径
                    _filePaths[id] = filePath;
                }
            }
            else
            {
                // 新画布，直接添加
                _filePaths[id] = filePath;
            }

            CurrentFilePath = _filePaths[id];
        }

        /// <summary>
        /// 检查指定文件路径是否已被其他画布打开
        /// </summary>
        /// <param name="filePath">要检查的文件路径</param>
        /// <param name="currentCanvasId">当前画布 ID（排除自身）</param>
        /// <returns>true 表示文件已被其他画布打开</returns>
        private bool IsFileOpenedByOtherCanvas(string filePath, int currentCanvasId)
        {
            foreach (var kvp in _filePaths)
            {
                if (kvp.Key != currentCanvasId
                    && string.Equals(kvp.Value, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void TryRestoreStoredParameters(int canvasId, CanvasStorageDocumentDto document)
        {
            if (_markParam == null)
            {
                return;
            }

            try
            {
                var restoredPairs = MarkingParamHelper.RestoreParameters(document.ExtensionPayloads);
                if (restoredPairs.Count > 0)
                {
                    _markParam.SetParameters(canvasId, restoredPairs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "恢复显式打标参数失败");
            }
        }
    }
}