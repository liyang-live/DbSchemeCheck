﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DbSchemeCheck.Properties;
using Newtonsoft.Json;

namespace DbSchemeCheck
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private string GetDbName(string connString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connString);
                return builder.InitialCatalog;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal class ExportSetting
        {
            public string TargetConnString { get; set; }

            public string TargetDbName { get; set; }

            public string JsonFile { get; set; }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (exportWorker.IsBusy)
            {
                return;
            }
            var setting = new ExportSetting();
            if (string.IsNullOrEmpty(targetConnStringBox.Text))
            {
                MessageBox.Show("目标数据库" + Resources.ConnStringEmptyMsg);
                return;
            }
            var db = GetDbName(targetConnStringBox.Text);
            if (string.IsNullOrEmpty(db))
            {
                MessageBox.Show("目标数据库" + Resources.DbNameEmptyMsg);
                return;
            }
            setting.TargetConnString = targetConnStringBox.Text;
            setting.TargetDbName = db;
            var jsonFile = "";
            if (saveJsonFileDialog.ShowDialog() == DialogResult.OK)
            {
                jsonFile = saveJsonFileDialog.FileName;
                if (File.Exists(jsonFile))
                {
                    if (MessageBox.Show("文件已经存在，是否覆盖?", "文件覆盖提示", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    {
                        MessageBox.Show("导出信息已经被取消。");
                        return;
                    }
                }
            }
            else
            {
                return;
            }
            setting.JsonFile = jsonFile;
            try
            {
                exportWorker.RunWorkerAsync(setting);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void exportWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            var setting = e.Argument as ExportSetting;
            if (setting == null)
            {
                return;
            }
            var tables = DbHelper.GetDbTables(setting.TargetConnString, setting.TargetDbName);
            foreach (var dbTable in tables)
            {
                dbTable.LoadColumns(setting.TargetConnString, setting.TargetDbName);
            }
            var json = JsonConvert.SerializeObject(tables, Formatting.Indented);
            if (File.Exists(setting.JsonFile))
            {
                try
                {
                    File.Delete(setting.JsonFile);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    return;
                }
            }
            File.WriteAllText(setting.JsonFile, json);
            e.Result = Resources.ExportSuccessMsg;
        }

        private void selectFileButton_Click(object sender, EventArgs e)
        {
            if (selectFileDlalog.ShowDialog() == DialogResult.OK)
            {
                jsonFilePathBox.Text = selectFileDlalog.FileName;
            }
        }

        private void ShowError(ref List<string> errorList, string msg)
        {
            errorList.Add($"{errorList.Count+1}: {msg}");
        }
        
        internal class CompareSetting
        {
            public string SrcFile { get; set; }

            public string SrcConnString { get; set; }

            public string SrcDbName { get; set; }

            public string TargetConnString { get; set; }

            public string TargetDbName { get; set; }
        }

        private void compairButton_Click(object sender, EventArgs e)
        {
            if (compareWorker.IsBusy)
            {
                return;
            }
            var setting = new CompareSetting();
            if (string.IsNullOrEmpty(jsonFilePathBox.Text))
            {
                if (string.IsNullOrEmpty(srcConnStringBox.Text))
                {
                    MessageBox.Show("原数据库" + Resources.ConnStringEmptyMsg);
                    return;
                }
                var db1 = GetDbName(srcConnStringBox.Text);
                if (string.IsNullOrEmpty(db1))
                {
                    MessageBox.Show("原数据库" + Resources.DbNameEmptyMsg);
                    return;
                }
                else
                {
                    setting.SrcConnString = srcConnStringBox.Text;
                    setting.SrcDbName = db1;
                }
            }
            else
            {
                if (!File.Exists(jsonFilePathBox.Text))
                {
                    MessageBox.Show(Resources.JsonFileNotExistMsg);
                    return;
                }
                setting.SrcFile = jsonFilePathBox.Text;
            }
            if (string.IsNullOrEmpty(targetConnStringBox.Text))
            {
                MessageBox.Show("目标数据库" + Resources.ConnStringEmptyMsg);
                return;
            }
            var db2 = GetDbName(targetConnStringBox.Text);
            if (string.IsNullOrEmpty(db2))
            {
                MessageBox.Show("目标数据库" + Resources.DbNameEmptyMsg);
                return;
            }
            else
            {
                setting.TargetConnString = targetConnStringBox.Text;
                setting.TargetDbName = db2;
            }
            compareWorker.RunWorkerAsync(setting);
        }

        private void compareWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            var errorList = new List<string>();
            outputBox.Text = "";
            var settings = e.Argument as CompareSetting;
            if (settings == null)
            {
                return;
            }

            #region 加载原数据库信息

            List<DbTable> source;
            if (string.IsNullOrEmpty(settings.SrcFile))
            {
                source = DbHelper.GetDbTables(settings.SrcConnString, settings.SrcDbName);
                foreach (var table in source)
                {
                    table.LoadColumns(settings.SrcConnString, settings.SrcDbName);
                }
            }
            else
            {
                try
                {
                    source = JsonConvert.DeserializeObject<List<DbTable>>(File.ReadAllText(settings.SrcFile));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message);
                    return;
                }
            }

            #endregion

            #region 加载目标数据库

            var target = DbHelper.GetDbTables(settings.TargetConnString, settings.TargetDbName);
            foreach (var dbTable in target)
            {
                dbTable.LoadColumns(settings.TargetConnString, settings.TargetDbName);
            }

            #endregion

            #region 比较数据表

            foreach (var sTable in source)
            {
                var tTable =
                    target.FirstOrDefault(
                        x => x.TableName.Equals(sTable.TableName, StringComparison.InvariantCultureIgnoreCase));
                if (tTable == null)
                {
                    ShowError(ref errorList, $"数据表 \"{sTable.TableName}\" 在目标数据库中不存在。");
                    continue;
                }

                #region 比较每一列

                foreach (var sColumn in sTable.Columns)
                {
                    var tColumn = tTable.Columns.FirstOrDefault(x => x.ColumnName.Equals(sColumn.ColumnName, StringComparison.InvariantCultureIgnoreCase));
                    if (tColumn == null)
                    {
                        ShowError(ref errorList, $"数据表 \"{sTable.TableName}\" 中的列 \"{sColumn.ColumnName}\" 在目标数据库中不存在。");
                        continue;
                    }
                    if (sColumn.IsPrimaryKey != tColumn.IsPrimaryKey)
                    {
                        ShowError(ref errorList, $"数据表 \"{sTable.TableName}\" 中的列 \"{sColumn.ColumnName}\" 在原数据库中 '主键' 属性已被改变。");
                        continue;
                    }
                    if (sColumn.IsNullable != tColumn.IsNullable)
                    {
                        ShowError(ref errorList, $"数据表 \"{sTable.TableName}\" 中的列 \"{sColumn.ColumnName}\" 在原数据库中 '可为空' 属性已被改变。");
                        continue;
                    }
                    if (sColumn.DbType != tColumn.DbType)
                    {
                        ShowError(ref errorList, $"数据表 \"{sTable.TableName}\" 中的列 \"{sColumn.ColumnName}\" 在原数据库中 '数据类型' 属性已被改变。");
                        continue;
                    }
                }
                foreach (var tColumn in tTable.Columns)
                {
                    var sColumn = sTable.Columns.FirstOrDefault(x => x.ColumnName.Equals(tColumn.ColumnName, StringComparison.InvariantCultureIgnoreCase));
                    if (sColumn == null)
                    {
                        ShowError(ref errorList, $"数据表 \"{tTable.TableName}\" 中的列 \"{tColumn.ColumnName}\"  在原数据库中已被删除。");
                        continue;
                    }
                }
                #endregion
            }
            foreach (var tTable in target)
            {
                var sTable = source.FirstOrDefault(x => x.TableName.Equals(tTable.TableName, StringComparison.InvariantCultureIgnoreCase));
                if (sTable == null)
                {
                    ShowError(ref errorList, $"数据表 \"{tTable.TableName}\" 在原数据库中已被删除。");
                    continue;
                }
            }
            if (errorList.Count > 0)
            {
                var msg = string.Join("\r\n", errorList);
                e.Result = msg;
            }
            else
            {
                e.Result = "检测已完成";
            }
            #endregion
        }

        private void compareWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            outputBox.Text = e.Result as string;
        }
    }
}