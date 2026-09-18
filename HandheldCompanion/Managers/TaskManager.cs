using HandheldCompanion.Shared;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Security.Principal;

namespace HandheldCompanion.Managers;

public static class TaskManager
{
    private static string TaskExecutable = string.Empty;

    // TaskManager vars
    private static Task? task;
    private static TaskDefinition? taskDefinition;
    private static TaskService? taskService;

    private static bool IsInitialized;

    public static event InitializedEventHandler? Initialized;
    public delegate void InitializedEventHandler();

    static TaskManager()
    {
    }

    public static void Start(string Executable)
    {
        if (IsInitialized)
            return;

        TaskExecutable = Executable;
        taskService = new TaskService();

        try
        {
            // get current task, if any, delete it
            task = taskService.FindTask("HandheldCompanion");
            if (task is not null)
                taskService.RootFolder.DeleteTask("HandheldCompanion");
        }
        catch { }

        try
        {
            // create a new task
            taskDefinition = TaskService.Instance.NewTask();
            taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
            taskDefinition.Principal.UserId = WindowsIdentity.GetCurrent().Name;
            taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            taskDefinition.Settings.MultipleInstances = TaskInstancesPolicy.Parallel;
            taskDefinition.Settings.Enabled = true;
            bool runAtStartup = ManagerFactory.settingsManager.GetBoolean("RunAtStartup");
            bool startMinimized = ManagerFactory.settingsManager.GetBoolean("StartMinimized");
            taskDefinition.Triggers.Add(new LogonTrigger() 
            { 
                UserId = WindowsIdentity.GetCurrent().Name, 
                Delay = TimeSpan.FromSeconds(3),
                Enabled = runAtStartup 
            });
            string workingDir = System.IO.Path.GetDirectoryName(TaskExecutable) ?? string.Empty;
            string? taskArgs = startMinimized ? "--minimized" : null;
            taskDefinition.Actions.Add(new ExecAction(TaskExecutable, taskArgs, workingDir));

            task = TaskService.Instance.RootFolder.RegisterTaskDefinition("HandheldCompanion", taskDefinition);
            task.Enabled = true;
            LogManager.LogInformation("TaskManager registered scheduled task 'HandheldCompanion' (Enabled=true, RunAtStartup={0}, StartMinimized={1})", runAtStartup, startMinimized);
        }
        catch (Exception ex)
        {
            LogManager.LogWarning("TaskManager failed to register scheduled task: {0}", ex.Message);
        }

        // raise events
        switch (ManagerFactory.settingsManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.settingsManager.Initialized += SettingsManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QuerySettings();
                break;
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "TaskManager");
    }

    private static void SettingsManager_Initialized()
    {
        QuerySettings();
    }

    private static void QuerySettings()
    {
        // manage events
        ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        SettingsManager_SettingValueChanged("RunAtStartup", ManagerFactory.settingsManager.GetString("RunAtStartup"), false, false);
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        // manage events
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "TaskManager");
    }

    private static void SettingsManager_SettingValueChanged(string name, object? value, bool temporary, bool initializing)
    {
        switch (name)
        {
            case "RunAtStartup":
                UpdateTask(Convert.ToBoolean(value));
                break;
            case "StartMinimized":
                UpdateTaskArgs(Convert.ToBoolean(value));
                break;
        }
    }

    private static void UpdateTaskArgs(bool startMinimized)
    {
        if (task is null)
            return;

        try
        {
            if (task.Definition.Actions.Count > 0 && task.Definition.Actions[0] is ExecAction execAction)
            {
                execAction.Arguments = startMinimized ? "--minimized" : null;
                task.RegisterChanges();
                LogManager.LogInformation("TaskManager updated ExecAction.Arguments to {0}", execAction.Arguments);
            }
        }
        catch (Exception ex)
        {
            LogManager.LogWarning("TaskManager failed to update task arguments: {0}", ex.Message);
        }
    }

    private static void UpdateTask(bool value)
    {
        if (task is null)
            return;

        try
        {
            if (task.Definition.Triggers.Count > 0)
            {
                task.Definition.Triggers[0].Enabled = value;
                task.RegisterChanges();
                LogManager.LogInformation("TaskManager updated LogonTrigger.Enabled to {0}", value);
            }
        }
        catch (Exception ex)
        {
            LogManager.LogWarning("TaskManager failed to update task trigger: {0}", ex.Message);
        }
    }
}
