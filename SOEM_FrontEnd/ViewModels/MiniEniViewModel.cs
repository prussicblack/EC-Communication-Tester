using CommunityToolkit.Mvvm.Input;
using SOEM_FrontEnd.Ethercat.MiniENI;
using System;

using System.Windows.Input;

namespace SOEM_FrontEnd.ViewModels;

public sealed class MiniEniViewModel : ViewModelBase
{

    private readonly Func<MiniENI> _currentMiniEniFactory;

    private string _miniEniPath = "";
    public string MiniEniPath
    {
        get { return _miniEniPath; }
        private set { SetProperty(ref _miniEniPath, value); }
    }

    private string _miniEniJson = "";
    public string MiniEniJson
    {
        get { return _miniEniJson; }
        set { SetProperty(ref _miniEniJson, value); }
    }

    private string _statusText = "";
    public string StatusText
    {
        get { return _statusText; }
        private set { SetProperty(ref _statusText, value); }
    }

    public ICommand RefreshCommand { get; private set; }
    public ICommand SaveCommand { get; private set; }
    public ICommand SaveCurrentCommand { get; private set; }

    public MiniEniViewModel(Func<MiniENI> currentMiniEniFactory)
    {
        _currentMiniEniFactory = currentMiniEniFactory;

        RefreshCommand = new RelayCommand(RefreshFromCurrent);
        SaveCommand = new RelayCommand(SaveToDefaultFile);
        SaveCurrentCommand = new RelayCommand(SaveCurrentToDefaultFile);

        RefreshFromCurrent();
    }

    public void RefreshFromCurrent()
    {
        MiniEniPath = MiniENICatalog.GetDefaultPath();

        MiniENI current = MiniENICatalog.Current;

        if (current == null)
        {
            MiniEniJson = "";
            StatusText = "MiniENI is not loaded.";
            return;
        }

        MiniEniJson = MiniENIJson.Serialize(current);
        StatusText = "MiniENI loaded.";
    }

    private void SaveToDefaultFile()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(MiniEniJson))
            {
                StatusText = "MiniENI JSON is empty.";
                return;
            }

            MiniENI project = MiniENIJson.Deserialize(MiniEniJson);

            if (project == null)
            {
                StatusText = "MiniENI deserialize failed.";
                return;
            }

            if (project.Adapter == null)
            {
                project.Adapter = new EniAdapterConfig();
            }

            if (project.Slaves == null)
            {
                project.Slaves = new System.Collections.Generic.List<EniSlaveConfig>();
            }

            string message;
            bool ok = MiniENICatalog.TrySaveDefault(project, out message);

            StatusText = message;

            if (ok)
            {
                RefreshFromCurrent();
            }
        }
        catch (Exception ex)
        {
            StatusText = "MiniENI save failed: " + ex.Message;
        }
    }

    private void SaveCurrentToDefaultFile()
    {
        try
        {
            if (_currentMiniEniFactory == null)
            {
                StatusText = "Current MiniENI factory is null.";
                return;
            }

            MiniENI project = _currentMiniEniFactory();

            if (project == null)
            {
                StatusText = "Current MiniENI build failed.";
                return;
            }

            string message;
            bool ok = MiniENICatalog.TrySaveDefault(project, out message);

            StatusText = message;

            if (ok)
            {
                RefreshFromCurrent();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Save current MiniENI failed: " + ex.Message;
        }
    }
}