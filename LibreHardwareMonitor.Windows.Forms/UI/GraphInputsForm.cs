// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public sealed class GraphInputsForm : Form
{
    private readonly Action _inputsChanged;
    private readonly BindingSource _bindingSource = new();
    private readonly List<GraphInputRow> _rows;
    private readonly TextBox _searchTextBox = new();
    private readonly CheckBox _showHiddenCheckBox = new();
    private readonly DataGridView _grid = new();
    private readonly Timer _refreshTimer = new();
    private bool _suspendInputsChanged;

    public GraphInputsForm(IEnumerable<SensorNode> sensorNodes, Action inputsChanged)
    {
        _inputsChanged = inputsChanged;
        _rows = sensorNodes.Select(node => new GraphInputRow(node, InputsChanged)).ToList();

        InitializeComponent();
        ApplyTheme();
        RebuildFilter();

        _refreshTimer.Interval = 1000;
        _refreshTimer.Tick += delegate { RefreshRows(); };
        _refreshTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _bindingSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        Text = "Graph Inputs";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MinimumSize = new Size(760, 420);
        ClientSize = new Size(900, 520);

        Label searchLabel = new()
        {
            AutoSize = true,
            Location = new Point(12, 15),
            Text = "Search:"
        };

        _searchTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _searchTextBox.Location = new Point(65, 12);
        _searchTextBox.Size = new Size(620, 23);
        _searchTextBox.TextChanged += delegate { RebuildFilter(); };

        _showHiddenCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _showHiddenCheckBox.AutoSize = true;
        _showHiddenCheckBox.Location = new Point(705, 14);
        _showHiddenCheckBox.Text = "Show hidden sensors";
        _showHiddenCheckBox.CheckedChanged += delegate { RebuildFilter(); };

        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.DataSource = _bindingSource;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.Location = new Point(12, 44);
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ShowCellErrors = false;
        _grid.ShowRowErrors = false;
        _grid.Size = new Size(876, 424);
        _grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
        _grid.CellValueChanged += delegate { CommitCurrentEdit(); };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DataPropertyName = nameof(GraphInputRow.On),
            HeaderText = "On",
            Name = "OnColumn",
            Width = 42
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(GraphInputRow.SensorPath),
            HeaderText = "Sensor",
            Name = "SensorColumn",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DataPropertyName = nameof(GraphInputRow.CurrentValue),
            HeaderText = "Current Value",
            Name = "CurrentValueColumn",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = 115
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DataPropertyName = nameof(GraphInputRow.Unit),
            HeaderText = "Unit",
            Name = "UnitColumn",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DataPropertyName = nameof(GraphInputRow.Type),
            HeaderText = "Type",
            Name = "TypeColumn",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = 105
        });

        Button clearAllButton = CreateButton("Clear All", 560);
        clearAllButton.Click += delegate { SetRows(_rows, false); };

        Button selectVisibleButton = CreateButton("Select Visible", 640);
        selectVisibleButton.Click += delegate { SetRows(CurrentRows(), true); };

        Button applyButton = CreateButton("Apply", 750);
        applyButton.Click += delegate
        {
            CommitCurrentEdit();
            RefreshRows();
            _inputsChanged?.Invoke();
        };

        Button closeButton = CreateButton("Close", 825);
        closeButton.DialogResult = DialogResult.OK;
        closeButton.Click += delegate { Close(); };

        AcceptButton = closeButton;
        CancelButton = closeButton;

        Controls.Add(searchLabel);
        Controls.Add(_searchTextBox);
        Controls.Add(_showHiddenCheckBox);
        Controls.Add(_grid);
        Controls.Add(clearAllButton);
        Controls.Add(selectVisibleButton);
        Controls.Add(applyButton);
        Controls.Add(closeButton);
    }

    private Button CreateButton(string text, int left)
    {
        return new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(left, 480),
            Size = new Size(text.Length > 9 ? 100 : 75, 27),
            Text = text,
            UseVisualStyleBackColor = true
        };
    }

    private void ApplyTheme()
    {
        Theme.Current.Apply(this);

        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = Theme.Current.TreeBackgroundColor;
        _grid.GridColor = Theme.Current.TreeRowSepearatorColor;
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Current.TreeBackgroundColor,
            ForeColor = Theme.Current.TreeTextColor,
            SelectionBackColor = Theme.Current.TreeSelectedBackgroundColor,
            SelectionForeColor = Theme.Current.TreeSelectedTextColor
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Current.BackgroundColor,
            ForeColor = Theme.Current.ForegroundColor,
            SelectionBackColor = Theme.Current.SelectedBackgroundColor,
            SelectionForeColor = Theme.Current.SelectedForegroundColor
        };
    }

    private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
    {
        if (_grid.CurrentCell is DataGridViewCheckBoxCell)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void CommitCurrentEdit()
    {
        if (_grid.IsCurrentCellDirty)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _grid.EndEdit();
    }

    private void InputsChanged()
    {
        if (_suspendInputsChanged)
            return;

        _inputsChanged?.Invoke();
    }

    private void SetRows(IEnumerable<GraphInputRow> rows, bool on)
    {
        CommitCurrentEdit();

        // Apply the bulk change without firing the (expensive) graph recompute per row, then
        // notify exactly once. Avoids the O(N^2) PlotSelectionChanged fan-out of toggling each row.
        _suspendInputsChanged = true;
        try
        {
            foreach (GraphInputRow row in rows.ToList())
                row.On = on;
        }
        finally
        {
            _suspendInputsChanged = false;
        }

        RefreshRows();
        _inputsChanged?.Invoke();
    }

    private IEnumerable<GraphInputRow> CurrentRows()
    {
        return _bindingSource.List.Cast<GraphInputRow>();
    }

    private void RefreshRows()
    {
        // The dialog is modal, so Plot only changes from within it; only the rows currently
        // bound to the grid are visible, so refreshing the filtered-out rows every tick is waste.
        foreach (GraphInputRow row in CurrentRows())
            row.Refresh();

        _grid.Refresh();
    }

    private void RebuildFilter()
    {
        string filter = _searchTextBox.Text.Trim();
        bool showHidden = _showHiddenCheckBox.Checked;

        IEnumerable<GraphInputRow> rows = _rows.Where(row => showHidden || !row.Hidden);
        if (!string.IsNullOrEmpty(filter))
        {
            rows = rows.Where(row =>
                row.SensorPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.Type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.CurrentValue.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.Unit.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        _bindingSource.DataSource = new BindingList<GraphInputRow>(rows.ToList());
    }

    private sealed class GraphInputRow : INotifyPropertyChanged
    {
        private readonly SensorNode _node;
        private readonly Action _changed;
        private bool _on;
        private bool _hidden;
        private string _currentValue;
        private string _unit;

        public GraphInputRow(SensorNode node, Action changed)
        {
            _node = node;
            _changed = changed;
            SensorPath = BuildSensorPath(node);
            Type = node.Sensor.SensorType.ToString();
            Refresh();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool On
        {
            get { return _on; }
            set
            {
                if (_on == value)
                    return;

                _on = value;
                _node.Plot = value;
                OnPropertyChanged(nameof(On));
                _changed?.Invoke();
            }
        }

        public string SensorPath { get; }

        public string CurrentValue
        {
            get { return _currentValue; }
            private set
            {
                if (_currentValue == value)
                    return;

                _currentValue = value;
                OnPropertyChanged(nameof(CurrentValue));
            }
        }

        public string Unit
        {
            get { return _unit; }
            private set
            {
                if (_unit == value)
                    return;

                _unit = value;
                OnPropertyChanged(nameof(Unit));
            }
        }

        public string Type { get; }

        public bool Hidden
        {
            get { return _hidden; }
            private set
            {
                if (_hidden == value)
                    return;

                _hidden = value;
                OnPropertyChanged(nameof(Hidden));
            }
        }

        public void Refresh()
        {
            if (_on != _node.Plot)
            {
                _on = _node.Plot;
                OnPropertyChanged(nameof(On));
            }

            Hidden = !_node.IsVisible;

            SplitValueAndUnit(_node.Value, out string currentValue, out string unit);
            CurrentValue = currentValue;
            Unit = unit;
        }

        private static string BuildSensorPath(Node node)
        {
            Stack<string> parts = new();
            for (Node current = node; current != null; current = current.Parent)
            {
                if (current.Parent != null)
                    parts.Push(current.Text);
            }

            return string.Join(" / ", parts);
        }

        private static void SplitValueAndUnit(string value, out string currentValue, out string unit)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "-")
            {
                currentValue = "-";
                unit = string.Empty;
                return;
            }

            value = value.Trim();

            // Treat the trailing run of non-numeric characters as the unit (e.g. "61.5 °C" -> "°C",
            // "100Mbps" -> "Mbps"). Stop at the first digit/sign so a culture that uses a space as a
            // group separator (e.g. "1 234.5") is never mistaken for a value+unit pair.
            int unitStart = value.Length;
            while (unitStart > 0)
            {
                char c = value[unitStart - 1];
                if (char.IsDigit(c) || c == '.' || c == ',' || c == '-' || c == '+')
                    break;

                unitStart--;
            }

            string unitCandidate = value.Substring(unitStart).Trim();
            if (unitStart == 0 || unitCandidate.Length == 0)
            {
                currentValue = value;
                unit = string.Empty;
                return;
            }

            currentValue = value.Substring(0, unitStart).Trim();
            unit = unitCandidate;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
