using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CsvHelper;
using CsvHelper.Configuration;
using FolderBrowserEx;
using FxlHelper;
using FxlHelper.Entities.SurveyCsv;
using FxlHelper.Entities.TrimbleFxl;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace SurveyDataMagic
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            _inputSurveyCsvFullFilePaths = new List<string>();
            BtnClearSurveyCsv.IsEnabled = false;
            BtnParseSurveyFiles.IsEnabled = false;
        }

        private readonly List<string> _inputSurveyCsvFullFilePaths;
        private TrimbleFxl _fxl;
        private List<SurveyCsv> _surveyFiles;
        private List<SurveyCsvPointError> _pointErrors;

        private void BtnPickFxl_OnClick(object sender, RoutedEventArgs e)
        {
            var fbd = new OpenFileDialog
            {
                Title = "Select Trimble fxl file",
                Filter = "fxl file (*.fxl)|*.fxl",
            };
            if (fbd.ShowDialog() != true)
                return;

            var fxlFilepath = fbd.FileName;
            FxlFilePath.Text = fxlFilepath;
        }

        private void FxlFilePath_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var filePath = FxlFilePath.Text;
            var validFile = filePath != ""
                            && Path.HasExtension(filePath)
                            && Path.GetExtension(filePath) == ".fxl"
                            && File.Exists(filePath);
            if (validFile)
            {
                BtnParseSurveyFiles.IsEnabled = AllFormInputsValid();
                FxlFilePath.ToolTip = filePath;
                FxlFilePath.Foreground = Brushes.Black;
            }
            else
            {
                BtnParseSurveyFiles.IsEnabled = false;
                FxlFilePath.ToolTip = "Not a valid file!";
                FxlFilePath.Foreground = Brushes.Red;
            }
        }

        private void BtnAddSurveyCsv_OnClick(object sender, RoutedEventArgs e)
        {
            var fbd = new OpenFileDialog
            {
                Title = "Select survey csv files",
                Filter = "csv file (*.csv)|*.csv",
                Multiselect = true
            };
            if (fbd.ShowDialog() != true)
                return;
            var csvFiles = fbd.FileNames;
            foreach (var csvFile in csvFiles)
            {
                // if item is already in list there is no need to add so skip it
                if (_inputSurveyCsvFullFilePaths.Contains(csvFile))
                    continue;

                // item is not already in list so add it
                _inputSurveyCsvFullFilePaths.Add(csvFile);
                var item = new ListViewItem { Content = csvFile, ToolTip = csvFile };
                var menuItemDelete = new MenuItem
                {
                    Header = "Delete",
                    IsCheckable = false
                };
                menuItemDelete.Click += (_, _) =>
                {
                    _inputSurveyCsvFullFilePaths.Remove(csvFile);
                    SurveyCsvList.Items.Remove(item);
                    BtnClearSurveyCsv.IsEnabled = SurveyCsvList.Items.Count > 0;
                    BtnParseSurveyFiles.IsEnabled = SurveyCsvList.Items.Count > 0;
                };

                item.ContextMenu = new ContextMenu
                {
                    Items = { menuItemDelete }
                };
                SurveyCsvList.Items.Add(item);
            }

            BtnClearSurveyCsv.IsEnabled = true;
            BtnParseSurveyFiles.IsEnabled = AllFormInputsValid();
        }

        private void BtnClearSurveyCsv_OnClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to clear the Survey CSV files list?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
            if (result != MessageBoxResult.Yes)
                return;
            _inputSurveyCsvFullFilePaths.Clear();
            SurveyCsvList.Items.Clear();
            BtnParseSurveyFiles.IsEnabled = false;
            BtnClearSurveyCsv.IsEnabled = false;
        }

        private void BtnPickOutFolder_OnClick(object sender, RoutedEventArgs e)
        {
            // get output folder from user
            var fbd = new FolderBrowserDialog { AllowMultiSelect = false, Title = "Select output folder: " };
            if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            OutFolder.Text = fbd.SelectedFolder;
        }

        private void OutFolder_OnTextChanged_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var outFolderText = OutFolder.Text;
            var validFolder = outFolderText != ""
                              && Directory.Exists(outFolderText);
            if (validFolder)
            {
                BtnParseSurveyFiles.IsEnabled = AllFormInputsValid();
                OutFolder.ToolTip = outFolderText;
                OutFolder.Foreground = Brushes.Black;
            }
            else
            {
                BtnParseSurveyFiles.IsEnabled = false;
                OutFolder.ToolTip = "Not a valid folder!";
                OutFolder.Foreground = Brushes.Red;
            }
        }

        private void BtnParseSurveyFiles_OnClick(object sender, RoutedEventArgs e)
        {
            // initialize variables
            var outFolder = OutFolder.Text;
            var csvFileCount = SurveyCsvList.Items.Count;
            try
            {
                _fxl = new TrimbleFxl(FxlFilePath.Text);
            }
            catch (TrimbleFxlException exception)
            {
                MessageBox.Show(exception.Message, "FXL Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _surveyFiles = new List<SurveyCsv>();
            _pointErrors = new List<SurveyCsvPointError>();
            var fileAccessErrSb = new StringBuilder();

            // begin processing survey files
            foreach (ListViewItem item in SurveyCsvList.Items)
            {
                var surveyCsvFilePath = item.Content.ToString();
                var surveyCsv = new SurveyCsv(surveyCsvFilePath);

                // split combined points before validation
                surveyCsv.SplitCombinedPoints(_fxl);
                _fxl.ValidateSurveyCsv(surveyCsv);

                // remove control codes if user has selected that option
                if (CkBxRemoveControlCodes.IsChecked.HasValue && (bool)CkBxRemoveControlCodes.IsChecked)
                    surveyCsv.RemoveControlCodes(_fxl);

                // errors occured while attempting to parse the CSVs, write the errors to a csv in the same
                // file location as fxl file and alert the user
                if (surveyCsv.PointErrors.Count != 0)
                {
                    _pointErrors.AddRange(surveyCsv.PointErrors);
                    continue;
                }

                // parse was successful add this file to list to be written to output csv filepath
                _surveyFiles.Add(surveyCsv);
            }

            var goodCnt = _surveyFiles.Count;
            var parseCnt = 0;
            var fileAccessErrCnt = 0;

            // write successfully parsed out csv files and notify user of their number and location
            if (goodCnt > 0)
            {
                // export grouped by code
                if (CkBxGroupOutFilesBySurveyCode.IsChecked.HasValue && (bool)CkBxGroupOutFilesBySurveyCode.IsChecked)
                {
                    var allPts = _surveyFiles.SelectMany(x => x.Points).ToList();
                    var ftCodes = _fxl.FeatureCodes;
                    var ctrlCodes = _fxl.ControlCodeDefinitions.Select(x => x.Code);
                    foreach (var ftCode in ftCodes)
                    {
                        var curCodePts = allPts.Where(x => SurveyCsvPoint.RemoveControlCodes(
                            x.Code, _fxl.FeatureCodes, ctrlCodes) == ftCode).ToList();

                        // if there are no points for the current code just skip the next stuff
                        if (!curCodePts.Any())
                            continue;

                        // write points for this code to csv
                        var csvPath = Path.Combine(outFolder!, $"{ftCode}.csv");
                        try
                        {
                            using var streamWriter = new StreamWriter(csvPath);
                            var csvWriterConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                            {
                                HasHeaderRecord = false
                            };
                            using var csvWriter = new CsvWriter(streamWriter, csvWriterConfig);
                            csvWriter.Context.RegisterClassMap<SurveyCsvPointMap>();
                            csvWriter.WriteRecords(curCodePts);
                            parseCnt++;
                        }
                        catch (IOException exception)
                        {
                            if (exception.Message.StartsWith("The process cannot access the file"))
                            {
                                fileAccessErrCnt++;
                                fileAccessErrSb.AppendLine(exception.Message);
                                fileAccessErrSb.AppendLine("Please make sure this file is closed and try again.");
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }
                // export by original filename
                else
                {
                    foreach (var surveyFile in _surveyFiles)
                    {
                        // write points for this successfully parsed file
                        var outFileName = Path.GetFileName(surveyFile.FileName);
                        var csvPath = Path.Combine(outFolder!, $"{outFileName}");
                        try
                        {
                            using var streamWriter = new StreamWriter(csvPath);
                            var csvWriterConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                            {
                                HasHeaderRecord = false
                            };
                            using var csvWriter = new CsvWriter(streamWriter, csvWriterConfig);
                            csvWriter.Context.RegisterClassMap<SurveyCsvPointMap>();
                            csvWriter.WriteRecords(surveyFile.Points);
                            parseCnt++;
                        }
                        catch (IOException exception)
                        {
                            if (exception.Message.StartsWith("The process cannot access the file"))
                            {
                                goodCnt--;
                                fileAccessErrCnt++;
                                fileAccessErrSb.AppendLine(exception.Message);
                                fileAccessErrSb.AppendLine("Please make sure this file is closed and try again.");
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }
            }

            // provide user with parsing details
            var finalSb = new StringBuilder();
            var sameFileCnt = goodCnt == parseCnt;
            var msgTitle = "Success";
            var msgIcon = MessageBoxImage.Information;

            // add success message at start of message box string
            if (goodCnt > 0)
                finalSb.AppendLine($"{goodCnt} {(goodCnt > 1 ? "files" : "file")} " +
                                   $"successfully parsed{(sameFileCnt ? "." : $" into {parseCnt} files.")}");

            // add file access errors to the start of the message box message string
            if (fileAccessErrCnt > 0)
            {
                if (goodCnt > 0)
                {
                    msgTitle = "Partial Success";
                    msgIcon = MessageBoxImage.Exclamation;
                }
                else
                {
                    msgTitle = "Failure";
                    msgIcon = MessageBoxImage.Error;
                }

                finalSb.AppendLine();
                finalSb.AppendLine($"{fileAccessErrCnt} {(fileAccessErrCnt > 1 ? "files were" : "file was")} " +
                                   " unable to be written due to file access errors, details below.");
                finalSb.AppendLine(fileAccessErrSb.ToString());
            }

            // write point error details to csv log file
            // and add point errors to the end of the message box string
            var errCnt = _pointErrors.Count;
            if (errCnt > 0)
            {
                if (goodCnt > 0)
                {
                    msgTitle = "Partial Success";
                    msgIcon = MessageBoxImage.Exclamation;
                }
                else
                {
                    msgTitle = "Failure";
                    msgIcon = MessageBoxImage.Error;
                }

                // write error log to csv
                var errCsvPath = Path.Combine(outFolder,
                    $"SurveyCsvErrorReport_{DateTime.Now:yyyy_MM_dd_hh_mm_ss}.csv");
                using var streamWriter = new StreamWriter(errCsvPath);
                using var csvWriter = new CsvWriter(streamWriter, CultureInfo.InvariantCulture);
                csvWriter.Context.RegisterClassMap<SurveyCsvPointErrorMap>();
                csvWriter.WriteRecords(_pointErrors);

                // provide details about errors found and log file created
                finalSb.AppendLine();
                finalSb.AppendLine(
                    $"There {(errCnt > 1 ? $"were validation errors on {errCnt} points" : "was a validation error on a point")} " +
                    $"while attempting to parse the selected survey CSV {(csvFileCount > 1 ? "files" : " file")}.");
                finalSb.AppendLine($"A detailed report was written here: {errCsvPath}");
                finalSb.AppendLine("Please review the report, fix the errors, and try again.");
            }

            MessageBox.Show(finalSb.ToString(), msgTitle, MessageBoxButton.OK, msgIcon);
        }

        private bool AllFormInputsValid()
        {
            var filePath = FxlFilePath.Text;
            var outFolderText = OutFolder.Text;
            return filePath != ""
                   && Path.HasExtension(filePath)
                   && Path.GetExtension(filePath) == ".fxl"
                   && File.Exists(filePath)
                   && outFolderText != ""
                   && Directory.Exists(outFolderText)
                   && SurveyCsvList.Items.Count > 0;
        }
    }
}