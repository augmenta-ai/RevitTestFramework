﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using Autodesk.RevitAddIns;
using Microsoft.Practices.Prism;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.ViewModel;
using RTF.Applications.Properties;
using RTF.Framework;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace RTF.Applications
{
    public interface IContext
    {
        bool IsSynchronized { get; }
        void Invoke(Action action);
        void BeginInvoke(Action action);
    }

    public sealed class WpfContext : IContext
    {
        private readonly Dispatcher _dispatcher;

        public bool IsSynchronized
        {
            get
            {
                return this._dispatcher.Thread == Thread.CurrentThread;
            }
        }

        public WpfContext()
            : this(Dispatcher.CurrentDispatcher)
        {
        }

        public WpfContext(Dispatcher dispatcher)
        {
            Debug.Assert(dispatcher != null);

            this._dispatcher = dispatcher;
        }

        public void Invoke(Action action)
        {
            Debug.Assert(action != null);

            this._dispatcher.Invoke(action);
        }

        public void BeginInvoke(Action action)
        {
            Debug.Assert(action != null);

            this._dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>
    /// The Runner's view model.
    /// </summary>
    public class RunnerViewModel : NotificationObject
    {
        #region private members

        private object selectedItem;
        private Runner runner;
        private bool isRunning = false;
        private object isRunningLock = new object();
        private IContext context;
        private FileSystemWatcher watcher;
        private ObservableCollection<string> recentFiles = new ObservableCollection<string>();

        private bool workingDirSetByUser = false;
        private bool resultsFileSetByUser = false;

        private int passedTestCount;
        private int skippedTestCount;
        private int failedTestCount;

        #endregion

        #region public properties

        public object SelectedItem
        {
            get { return selectedItem; }
            set
            {
                selectedItem = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(RunText));
                RunCommand.RaiseCanExecuteChanged();
                ExportCommand.RaiseCanExecuteChanged();
            }
        }

        public int SelectedProductIndex
        {
            get
            {
                return IndexOfRevitProduct();
            }
        }

        public string RunText
        {
            get
            {
                if (SelectedItem is IAssemblyData)
                {
                    return "Run All Tests in Selected Assembly";
                }
                
                if (SelectedItem is IFixtureData)
                {
                    return "Run All Tests in Selected Fixture";
                }
                 
                if(SelectedItem is ITestData)
                {
                    return "Run Selected Test";
                }

                if (SelectedItem is ICategoryData)
                {
                    return "Run All Tests in Selected Category";
                }

                return "Nothing Selected";
            }
            set
            {
                RaisePropertyChanged();
            }
        }
        
        public bool IsRunning
        {
            get
            {
                lock (isRunningLock)
                {
                    return isRunning;  
                }
            }
            set
            {
                lock (isRunningLock)
                {
                    isRunning = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string SelectedTestSummary
        {
            get
            {
                if (runner == null) return string.Empty;

                var allTests = runner.GetAllTests();
                var selectedTests = runner.GetRunnableTests();

                return string.Format($"{selectedTests.Count()} (out of {allTests.Count()})");
            }
        }

        /// <summary>
        /// Number of tests that passed from current/last run
        /// </summary>
        public int PassedTestCount
        {
            get
            {
                return passedTestCount;
            }
            set
            {
                if (passedTestCount != value)
                {
                    passedTestCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Number of tests that were skipped from current/last run
        /// </summary>
        public int SkippedTestCount
        {
            get
            {
                return skippedTestCount;
            }
            set
            {
                if (skippedTestCount != value)
                {
                    skippedTestCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Number of tests that failed as of current/last run
        /// </summary>
        public int FailedTestCount
        {
            get
            {
                return failedTestCount;
            }
            set
            {
                if (failedTestCount != value)
                {
                    failedTestCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        public ObservableCollection<string> RecentFiles
        {
            get { return recentFiles; }
            set
            {
                recentFiles = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasRecentFiles));
            }
        }

        public bool HasRecentFiles
        {
            get { return recentFiles != null && recentFiles.Any(); }
        }

        public ObservableCollection<IAssemblyData> Assemblies
        {
            get { return runner.Assemblies; }
        }

        public ObservableCollection<RevitProduct> Products
        {
            get { return runner.Products; }
        }

        public bool IsDebug
        {
            get { return runner.IsDebug; }
            set
            {
                runner.IsDebug = value;
                RaisePropertyChanged();
            }
        }

        public bool Concat
        {
            get { return runner.Concat; }
            set
            {
                runner.Concat = value;
                RaisePropertyChanged();
            }
        }

        public int Timeout
        {
            get { return runner.Timeout; }
            set
            {
                runner.Timeout = value;
                RaisePropertyChanged();
            }
        }

        public string WorkingDirectory
        {
            get { return runner.WorkingDirectory; }
            set
            {
                runner.WorkingDirectory = value;
                runner.InitializeTests();
                UpdateTestCounts();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Assemblies));
            }
        }

        public string JournalSample
        {
            get { return runner.JournalSample; }
            set
            {
                runner.JournalSample = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// true to run tests without restarting Revit in between each tests
        /// </summary>
        public bool Continuous
        {
            get { return runner.Continuous; }
            set
            {
                runner.Continuous = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// true to group tests by model path
        /// When grouped, tests with the same model path will run without reopening the .RVT file
        /// Requires <see cref="Continuous"/>
        /// </summary>
        public bool GroupByModel
        {
            get { return runner.GroupByModel; }
            set
            {
                if (runner.GroupByModel != value)
                {
                    runner.GroupByModel = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// True to export the journal file for each selected test
        /// </summary>
        public bool IsExport
        {
            get { return runner.IsExport; }
            set
            {
                if (runner.IsExport != value)
                {
                    runner.IsExport = value;
                    RaisePropertyChanged();
                    ExportCommand.RaiseCanExecuteChanged();
                }
                
            }
        }

        public GroupingType GroupingType
        {
            get { return runner.GroupingType; }
            set
            {
                runner.GroupingType = value;
                runner.InitializeTests();
                UpdateTestCounts();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Assemblies));
            }
        }

        public string TestAssembly
        {
            get { return runner.TestAssembly; }
            set
            {
                runner.TestAssembly = value;

                // Infer working directory from assembly, if the user hasn't set it yet
                if (!workingDirSetByUser)
                {
                    SetWorkingDirectory(runner.TestAssembly);
                }

                if (!resultsFileSetByUser)
                {
                    SetResultsPath(Path.Combine(WorkingDirectory, "results.xml"));
                }

                runner.InitializeTests();
                UpdateTestCounts();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Assemblies));
                RaisePropertyChanged(nameof(SelectedProductIndex));
            }
        }

        public string Results
        {
            get { return runner.Results; }
            set
            {
                runner.Results = value;
                RaisePropertyChanged();
            }
        }

        public string AdditionalResolutionDirectoriesText
        {
            get
            {
                return string.Join(";", runner.AdditionalResolutionDirectories);
            }
            set
            {
                var splits = value.Split(new []{';'}, StringSplitOptions.RemoveEmptyEntries);
                if (!splits.Any()) return;

                runner.AdditionalResolutionDirectories.Clear();
                foreach (var split in splits)
                {
                    runner.AdditionalResolutionDirectories.Add(split);
                }
                RaisePropertyChanged();
            }
        } 
        #endregion

        #region commands

        public DelegateCommand<string> SetAssemblyPathCommand { get; set; }
        public DelegateCommand<string> SetResultsPathCommand { get; set; }
        public DelegateCommand<string> SetWorkingPathCommand { get; set; }
        public DelegateCommand<string> SetJournalSampleCommand { get; set; }
        public DelegateCommand<object> RunCommand { get; set; }
        public DelegateCommand ExportCommand { get; set; }
        public DelegateCommand CleanupCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }
        public DelegateCommand UpdateCommand { get; set; }
        public DelegateCommand SaveCommand { get; set; }
        public DelegateCommand<object> OpenFileCommand { get; set; }
        public DelegateCommand<object> ChangeProductCommand { get; set; }

        #endregion

        #region constructors

        internal RunnerViewModel(IContext context)
        {
            this.context = context;

            InitializeRecentFiles();

            InitializeRunner();

            InitializeCommands();

            // Make some convenient defaults
            Continuous = true;
            GroupByModel = true;
        }

        private void InitializeRunner()
        {
            runner = new Runner();

            InitializeEventHandlers();

            if (runner != null) return;

            MessageBox.Show("The runner could not be created with the specified inputs.");
            throw new Exception();
        }

        #endregion

        #region setup

        private void InitializeRecentFiles()
        {
            if (Settings.Default.recentFiles != null)
            {
                RecentFiles.AddRange(Settings.Default.recentFiles.Cast<string>());
            }
        }

        private void InitializeFileWatcher()
        {
            if (runner == null || string.IsNullOrEmpty(runner.AssemblyPath)) return;

            watcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(runner.AssemblyPath),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(runner.AssemblyPath)
            };
            watcher.Changed += watcher_Changed;
            watcher.EnableRaisingEvents = true;
        }

        private void DeinitializeFileWatcher()
        {
            if (watcher == null) return;
            watcher.Changed -= watcher_Changed;
            watcher.EnableRaisingEvents = false;
            watcher = null;
        }

        private void InitializeEventHandlers()
        {
            runner.Assemblies.CollectionChanged += Assemblies_CollectionChanged;
            runner.Products.CollectionChanged += Products_CollectionChanged;
            runner.TestComplete += runner_TestComplete;
            runner.TestFailed += runner_TestFailed;
            runner.TestTimedOut += runner_TestTimedOut;
            runner.Initialized += runner_Initialized;
            runner.TestRunsComplete += runner_TestRunsComplete;
        }

        private void RemoveEventHandlers()
        {
            runner.Assemblies.CollectionChanged -= Assemblies_CollectionChanged;
            runner.Products.CollectionChanged -= Products_CollectionChanged;
            runner.TestComplete -= runner_TestComplete;
            runner.TestFailed -= runner_TestFailed;
            runner.TestTimedOut -= runner_TestTimedOut;
            runner.Initialized -= runner_Initialized;
            runner.TestRunsComplete -= runner_TestRunsComplete;
        }

        private void InitializeCommands()
        {
            SetAssemblyPathCommand = new DelegateCommand<string>(SetAssemblyPath, CanSetAssemblyPath);
            SetResultsPathCommand = new DelegateCommand<string>(SetResultsPath, CanSetResultsPath);
            SetWorkingPathCommand = new DelegateCommand<string>(SetWorkingDirectory, CanSetWorkingPath);
            SetJournalSampleCommand = new DelegateCommand<string>(SetJournalSamplePath, CanSetJournalSample);
            RunCommand = new DelegateCommand<object>(Run, CanRun);
            ExportCommand = new DelegateCommand(ExportJournal, CanExport);
            CleanupCommand = new DelegateCommand(runner.Cleanup, CanCleanup);
            CancelCommand = new DelegateCommand(Cancel, CanCancel);
            UpdateCommand = new DelegateCommand(Update, CanUpdate);
            SaveCommand = new DelegateCommand(Save, CanSave);
            OpenFileCommand = new DelegateCommand<object>(OpenFile, CanOpenFile);
            ChangeProductCommand = new DelegateCommand<object>(ChangeProduct, CanChangeProduct);
        }


        #endregion

        #region event handlers

        void runner_Initialized(object sender, EventArgs e)
        {
            RaisePropertyChanged("");
        }

        private void watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!isRunning)
            {
                context.BeginInvoke(() => runner.InitializeTests());
            }
        }

        private void runner_TestTimedOut(ITestData data)
        {
            context.BeginInvoke(() =>
            {
                Runner.Runner_TestTimedOut(data);
                UpdateTestCounts();
            });
        }

        private void runner_TestFailed(ITestData data, string message, string stackTrace)
        {
            context.BeginInvoke(() =>
            {
                Runner.Runner_TestFailed(data, message, stackTrace);
                UpdateTestCounts();
            });
        }

        private void runner_TestComplete(IEnumerable<ITestData> data, string resultsPath)
        {
            context.BeginInvoke(() => 
            {
                Runner.GetTestResultStatus(data, resultsPath);
                UpdateTestCounts();
            });
        }

        private void runner_TestRunsComplete(object sender, EventArgs e)
        {
            context.BeginInvoke(() =>
            {
                UpdateTestCounts();

                bool isError = (FailedTestCount > 0);
                bool isWarning = (SkippedTestCount > 0);

                if (isError)
                {
                    Console.WriteLine($"ERROR: Test run completed with errors: {PassedTestCount} passed, {SkippedTestCount} skipped, {FailedTestCount} failed");
                }
                else if (isWarning)
                {
                    Console.WriteLine($"WARNING: Test run completed with warnings: {PassedTestCount} passed, {SkippedTestCount} skipped, {FailedTestCount} failed");
                }
                else
                {
                    Console.WriteLine($"Test run completed successfully: {PassedTestCount} passed, {SkippedTestCount} skipped, {FailedTestCount} failed");
                }
            });
        }

        private void UpdateTestCounts()
        {
            var runnableTests = runner.GetRunnableTests();

            var successValues = new List<TestStatus>
            {
                TestStatus.Success
            };

            var skippedValues = new List<TestStatus>
            {
                TestStatus.Ignored,
                TestStatus.Skipped,
                TestStatus.Cancelled
            };

            var failedValues = new List<TestStatus>
            {
                TestStatus.Failure,
                TestStatus.Error,
                TestStatus.Inconclusive,
                TestStatus.TimedOut,
                TestStatus.NotRunnable
            };

            PassedTestCount = runnableTests.Count(x => successValues.Contains(x.TestStatus));
            SkippedTestCount = runnableTests.Count(x => skippedValues.Contains(x.TestStatus));
            FailedTestCount = runnableTests.Count(x => failedValues.Contains(x.TestStatus));

            // Expand all the failed tests:
            foreach (var assembly in Assemblies)
            {
                foreach (var category in (GroupingType == GroupingType.Fixture) ? assembly.Fixtures : assembly.Categories)
                {
                    foreach (var test in category.Tests)
                    {
                        bool testFailed = (test.TestStatus != TestStatus.None) && (!successValues.Contains(test.TestStatus));
                        bool modelMissing = !((TestData)test).ModelExists;

                        if ((test.ShouldRun ?? false) && (testFailed || modelMissing))
                        {
                            ((TestData)test).IsNodeExpanded = true;

                            if (category is FixtureData fd)
                            {
                                fd.IsNodeExpanded = true;
                            }
                            else if (category is CategoryData cd)
                            {
                                cd.IsNodeExpanded = true;
                            }
                            ((AssemblyData)assembly).IsNodeExpanded = true;
                        }
                    }
                }
            }
        }

        void Products_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // When the products collection is changed, we want to set
            // the selected product index to the first in the list
            //if (runner.Products.Count > 0)
            //{
            //    SelectedProductIndex = 0;
            //}
            //else
            //{
            //    SelectedProductIndex = -1;
            //}

            RaisePropertyChanged(nameof(Products));
        }

        void Assemblies_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(Assemblies));
        }

        #endregion

        #region private methods

        private int IndexOfRevitProduct()
        {
            if (string.IsNullOrEmpty(runner.RevitPath))
                return -1;

            var found =
                Products.FirstOrDefault(
                    x =>
                        System.String.CompareOrdinal(
                            Path.GetDirectoryName(x.InstallLocation), Path.GetDirectoryName(runner.RevitPath)) ==
                        0);
            var index =
                Products.IndexOf(found);
            return index;
        }

        private bool CanRun(object parameter)
        {
            return !IsRunning && runner.GetRunnableTests().Any();
        }

        private void Run(object parameter)
        {
            if (string.IsNullOrEmpty(runner.Results))
            {
                MessageBox.Show("Please select an output path for the results.");
                return;
            }

            if (File.Exists(runner.Results) && !runner.Concat)
            {
                File.Delete(runner.Results);
            }

            PassedTestCount = 0;
            FailedTestCount = 0;
            SkippedTestCount = 0;

            var worker = new BackgroundWorker();

            worker.DoWork += TestThread;
            worker.RunWorkerAsync(parameter);   
        }

        private void TestThread(object sender, DoWorkEventArgs e)
        {
            IsRunning = true;

            runner.StartServer();
            if (runner.SetupTests())
            {
                runner.RunAllTests();
            }
            else
            {
                Console.WriteLine("ERROR: No tests were run due to configuration problems");
            }
            runner.EndServer();

            IsRunning = false;
        }

        private bool CanSetWorkingPath(string path)
        {
            return true;
        }

        private void SetWorkingDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                var dirs = new FolderBrowserDialog();

                if (dirs.ShowDialog() == DialogResult.OK)
                {
                    WorkingDirectory = dirs.SelectedPath;
                    workingDirSetByUser = true;
                }
            }
            else
            {
                if (File.Exists(path))
                {
                    // You've been given a file. Use its directory.
                    var fi = new FileInfo(path);
                    WorkingDirectory = fi.DirectoryName;
                    return;
                }

                if (!Directory.Exists(path))
                {
                    return;
                }

                WorkingDirectory = path;
            }
        }

        private bool CanSetResultsPath(string path)
        {
            return true;
        }

        private void SetResultsPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                var files = new SaveFileDialog()
                {
                    Filter = "xml files (*.xml) | *.xml",
                    RestoreDirectory = true,
                    DefaultExt = ".xml"
                };

                if (Directory.Exists(Path.GetDirectoryName(TestAssembly)))
                {
                    files.InitialDirectory = TestAssembly;
                }
                else
                {
                    files.InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                }

                var filesResult = files.ShowDialog();

                if (filesResult != null && filesResult == true)
                {
                    Results = files.FileName;
                    resultsFileSetByUser = true;
                    RunCommand.RaiseCanExecuteChanged();
                }
            }
            else
            {
                var fi = new FileInfo(path);
                if (fi.Extension != ".xml")
                {
                    return;
                }

                Results = path;
            }
        }

        private bool CanSetAssemblyPath(string path)
        {
            return true;
        }

        private void SetAssemblyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                var files = new OpenFileDialog
                {
                    Filter = "assembly files (*.dll)|*.dll| executable files (*.exe)|*.exe",
                    RestoreDirectory = true,
                    DefaultExt = ".dll"
                };

                var filesResult = files.ShowDialog();

                if (filesResult != null && filesResult == true)
                {
                    TestAssembly = files.FileName;
                }
            }
            else
            {
                if (!File.Exists(path))
                {
                    return;
                }

                TestAssembly = path;
            }
        }

        private bool CanCancel()
        {
            return true;
        }

        private void Cancel()
        {
            if (runner != null && IsRunning)
            {
                runner.CancelRequested = true;
            }

            IsRunning = false;
        }

        private void Update()
        {
            RaisePropertyChanged(nameof(SelectedTestSummary));
            RunCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
        }

        private bool CanUpdate()
        {
            return true;
        }

        private bool CanSave()
        {
            return !isRunning;
        }

        private void Save()
        {
            var sfd = new SaveFileDialog
            {
                InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Filter = "xml files (*.xml)|*.xml",
                DefaultExt = ".xml",
                AddExtension = true,
                RestoreDirectory = true
            };

            // Call the ShowDialog method to show the dialog box.
            var ok = sfd.ShowDialog();

            // Process input if the user clicked OK.
            if (ok != true) return;

            Runner.Save(sfd.FileName, runner);

            SaveRecentFile(sfd.FileName);
        }

        private void SaveRecentFile(string fileName)
        {
            // Clear the recent files list
            // and add this file to the top.
            RecentFiles.Clear();
            RecentFiles.Add(fileName);

            var i = 0;
            while (i < 2 && 
                Settings.Default.recentFiles != null &&
                Settings.Default.recentFiles.Count > i)
            {
                var recentPath = Settings.Default.recentFiles[i];

                // Only add the path if it isn't already
                // in the list.
                if (!RecentFiles.Contains(recentPath))
                {
                    RecentFiles.Add(Settings.Default.recentFiles[i]);
                }
                
                i++;
            }

            var sc = new StringCollection();
            sc.AddRange(RecentFiles.ToArray());
            Settings.Default.recentFiles = sc;
            Settings.Default.Save();
        }

        private bool CanOpenFile(object parameter)
        {
            return !IsRunning;
        }

        private void OpenFile(object parameter)
        {
            string fileName;

            // If the parameter is null, then this request
            // is coming from the file menu.
            if (parameter == null)
            {
                var ofd = new OpenFileDialog
                {
                    InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    Filter = "xml files (*.xml)|*.xml",
                    RestoreDirectory = true
                };

                // Call the ShowDialog method to show the dialog box.
                var ok = ofd.ShowDialog();

                // Process input if the user clicked OK.
                if (ok != true) return;

                fileName = ofd.FileName;
            }
            // Otherwise, the request is coming from the recent
            // files list.
            else
            {
                fileName = parameter.ToString();
                if (!File.Exists(fileName))
                {
                    MessageBox.Show("The specified file no longer exists.");
                    return;
                }
            }

            // Clear the runner
            if (runner != null)
            {
                DeinitializeFileWatcher();
                RemoveEventHandlers();
                runner.Dispose();
                runner = null;
            }

            runner = Runner.Load(fileName);

            if (runner == null)
            {
                Console.WriteLine("ERROR: Test session could not be opened.");
                runner = new Runner();
                return;
            }

            InitializeFileWatcher();

            InitializeEventHandlers();

            RaisePropertyChanged("");
            RaisePropertyChanged(nameof(SelectedProductIndex));

            SaveRecentFile(fileName);
        }

        private bool CanChangeProduct(object arg)
        {
            return true;
        }

        private void ChangeProduct(object obj)
        {
            var index = (int) obj;
            if (index != -1 && index < runner.Products.Count)
            {
                runner.RevitPath = Path.Combine(runner.Products[index].InstallLocation, "revit.exe");
            }
        }

        private bool CanCleanup()
        {
            return true;
        }

        private bool CanExport()
        {
            return runner.GetRunnableTests().Any();
        }

        private void ExportJournal()
        {
            if (Continuous || GroupByModel) 
            {
                MessageBox.Show("Can't export Continuous or GroupByModel journal file!");
                return;
            }
            if (String.IsNullOrEmpty(JournalSample) || !File.Exists(JournalSample)) 
            {
                MessageBox.Show("Should set a JournalSample File!");
                return;
            }
            var dirs = new FolderBrowserDialog();

            if (dirs.ShowDialog() == DialogResult.OK)
            {
                runner.ExportFolder = dirs.SelectedPath;
                runner.ExportJournal();
            }
        }

        private bool CanSetJournalSample(string path)
        {
            return true;
        }

        private void SetJournalSamplePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                var files = new OpenFileDialog()
                {
                    Filter = "txt files (*.txt) | *.txt",
                    RestoreDirectory = true,
                    DefaultExt = ".txt"
                };

                var filesResult = files.ShowDialog();

                if (filesResult != null && filesResult == true)
                {
                    JournalSample = files.FileName;
                }
            }
            else
            {
                var fi = new FileInfo(path);
                if (fi.Extension != ".txt")
                {
                    return;
                }

                JournalSample = path;
            }
        }

        #endregion

        protected override void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            base.RaisePropertyChanged(propertyName);
        }
    }
}
