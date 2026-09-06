using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using SekiroModManager.App.Common;
using SekiroModManager.Core.Models;

namespace SekiroModManager.App.ViewModels;

public class ModItemViewModel : ObservableObject
{
    private readonly ModItem _model;
    private readonly string _storageRoot;
    private readonly Action _onChanged;
    private readonly Action<ModItemViewModel> _onDelete;
    private string? _sizeFormatted;

    public ModItemViewModel(
        ModItem model,
        string storageRoot,
        Action onChanged,
        Action<ModItemViewModel> onDelete)
    {
        _model = model;
        _storageRoot = storageRoot;
        _onChanged = onChanged;
        _onDelete = onDelete;

        IncreasePriorityCommand = new RelayCommand(IncreasePriority);
        DecreasePriorityCommand = new RelayCommand(DecreasePriority);
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    public ModItem Model => _model;

    public string Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name != value)
            {
                _model.Name = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set
        {
            if (_model.Enabled != value)
            {
                _model.Enabled = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public int Priority
    {
        get => _model.Priority;
        set
        {
            var val = Math.Max(0, value);
            if (_model.Priority != val)
            {
                _model.Priority = val;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public string? SourcePath => _model.SourcePath;
    public string? SourceFileName => string.IsNullOrEmpty(_model.SourcePath) ? "-" : Path.GetFileName(_model.SourcePath);
    public string ImportedAtFormatted => _model.ImportedAt.ToString("yyyy-MM-dd HH:mm");

    public string StorageDirectory => Path.Combine(_storageRoot, _model.Id);

    public string SizeFormatted
    {
        get
        {
            if (_sizeFormatted != null)
                return _sizeFormatted;

            try
            {
                if (Directory.Exists(StorageDirectory))
                {
                    var bytes = Directory.EnumerateFiles(StorageDirectory, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
                    _sizeFormatted = FormatBytes(bytes);
                }
                else
                {
                    _sizeFormatted = "-";
                }
            }
            catch
            {
                _sizeFormatted = "-";
            }

            return _sizeFormatted;
        }
    }

    public ICommand IncreasePriorityCommand { get; }
    public ICommand DecreasePriorityCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public void IncreasePriority()
    {
        Priority += 10;
    }

    public void DecreasePriority()
    {
        Priority = Math.Max(0, Priority - 10);
    }

    public void OpenFolder()
    {
        try
        {
            if (!Directory.Exists(StorageDirectory))
                Directory.CreateDirectory(StorageDirectory);

            Process.Start(new ProcessStartInfo("explorer.exe", StorageDirectory) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
