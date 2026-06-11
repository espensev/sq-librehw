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
    private readonly Action<IEnumerable<SensorNode>, bool> _setPlot;
    private readonly BindingSource _bindingSource = new();
    private readonly BindingList<GraphInputRow> _visibleRows = new();
    private readonly List<GraphInputRow> _rows;
    private readonly TextBox _searchTextBox = new();
    private readonly CheckBox _showHiddenCheckBox = new();
    private readonly InputsGrid _grid = new();
    private readonly ContextMenuStrip _gridMenu = new();
    private readonly Timer _refreshTimer = new();
    private bool _swallowNextSpaceKeyUp;

    public GraphInputsForm(IEnumerable<SensorNode> sensorNodes, Action<IEnumerable<SensorNode>, bool> setPlot)
    {
        // All plot mutations — single checkbox edits and bulk actions alike — go through this
        // setter so the owner (MainForm) batches each user action into one graph rebuild.
        _setPlot = setPlot;
        _rows = sensorNodes.Select(node => new GraphInputRow(node, (sensorNode, plot) => setPlot(new[] { sensorNode }, plot))).ToList();

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
            _gridMenu.Dispose();
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
            // The label's mnemonic focuses the next control in tab order (the search box).
            Text = "&Search:"
        };

        _searchTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _searchTextBox.Location = new Point(65, 12);
        _searchTextBox.Size = new Size(620, 23);
        _searchTextBox.TextChanged += delegate { RebuildFilter(); };

        _showHiddenCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _showHiddenCheckBox.AutoSize = true;
        _showHiddenCheckBox.Location = new Point(705, 14);
        _showHiddenCheckBox.Text = "Show &hidden sensors";
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
        _bindingSource.DataSource = _visibleRows;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.Location = new Point(12, 44);
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ShowCellErrors = false;
        _grid.ShowRowErrors = false;
        _grid.Size = new Size(876, 424);
        _grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
        _grid.CellValueChanged += delegate { CommitCurrentEdit(); };
        _grid.KeyDown += Grid_KeyDown;
        _grid.KeyUp += Grid_KeyUp;
        _grid.CellMouseDown += Grid_CellMouseDown;

        _gridMenu.Opening += GridMenu_Opening;
        _grid.ContextMenuStrip = _gridMenu;

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

        Button clearAllButton = CreateButton("Clear &All", 560);
        clearAllButton.Click += delegate { SetRows(_rows, false); };

        Button selectVisibleButton = CreateButton("Select &Visible", 640);
        selectVisibleButton.Click += delegate { SetRows(CurrentRows(), true); };

        Button applyButton = CreateButton("A&pply", 750);
        applyButton.Click += delegate
        {
            CommitCurrentEdit();
            RefreshRows();
        };

        Button closeButton = CreateButton("&Close", 825);
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

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space)
            return;

        // Only a plain Space is the bulk plot-toggle verb: Shift/Ctrl+Space are framework
        // selection keys and Alt+Space is the system menu (WM_SYSKEYDOWN also lands here).
        // Mirrors the guard in MainForm.TreeView_KeyDown.
        if (e.Modifiers != Keys.None)
            return;

        // A single-row Space is handled normally by the checkbox cell; clear any flag stranded by
        // an earlier bulk KeyDown whose KeyUp never reached the grid (e.g. focus changed between
        // them), so this row's own KeyUp toggle is not silently eaten.
        if (_grid.SelectedRows.Count <= 1)
        {
            _swallowNextSpaceKeyUp = false;
            return;
        }

        List<GraphInputRow> rows = SelectedGraphInputRows();
        SetRows(rows, !rows.All(row => row.On));
        _swallowNextSpaceKeyUp = true;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Grid_KeyUp(object sender, KeyEventArgs e)
    {
        // The checkbox cell toggles on Space key-up; swallow it only when the paired KeyDown
        // performed the bulk toggle, so a selection that changed between key-down and key-up
        // cannot re-qualify and hand the current cell a stray single-cell flip.
        if (e.KeyCode == Keys.Space && _swallowNextSpaceKeyUp)
        {
            _swallowNextSpaceKeyUp = false;
            e.Handled = true;
        }
    }

    private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        if (!_grid.Rows[e.RowIndex].Selected)
        {
            _grid.ClearSelection();
            _grid.Rows[e.RowIndex].Selected = true;
            // Move the current cell off the checkbox column so EditOnEnter cannot start a
            // checkbox edit from a right-click.
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex > 0 ? e.ColumnIndex : 1];
        }
    }

    private void GridMenu_Opening(object sender, CancelEventArgs e)
    {
        // Items are recreated per opening; Clear() removes without disposing, so dispose the
        // previous set or every right-click strands finalizable components.
        ToolStripItem[] oldItems = new ToolStripItem[_gridMenu.Items.Count];
        _gridMenu.Items.CopyTo(oldItems, 0);
        _gridMenu.Items.Clear();
        foreach (ToolStripItem oldItem in oldItems)
            oldItem.Dispose();

        // A mouse-originated opening must come from a data cell: right-clicking the header or
        // the blank area below the last row must not act on a selection that may be stale or
        // scrolled off-screen. Keyboard invocation (Apps/Shift+F10) targets the selection as-is.
        if (!_grid.ContextMenuFromKeyboard)
        {
            Point client = _grid.PointToClient(_grid.ContextMenuScreenPoint);
            DataGridView.HitTestInfo hit = _grid.HitTest(client.X, client.Y);
            if (hit.Type != DataGridViewHitTestType.Cell || hit.RowIndex < 0 || hit.ColumnIndex < 0)
            {
                e.Cancel = true;
                return;
            }
        }

        List<GraphInputRow> rows = SelectedGraphInputRows();
        if (rows.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        if (rows.Any(row => !row.On))
        {
            ToolStripItem item = new ToolStripMenuItem($"Plot Selected ({rows.Count})");
            item.Click += delegate { SetRows(rows, true); };
            _gridMenu.Items.Add(item);
        }

        if (rows.Any(row => row.On))
        {
            ToolStripItem item = new ToolStripMenuItem($"Unplot Selected ({rows.Count})");
            item.Click += delegate { SetRows(rows, false); };
            _gridMenu.Items.Add(item);
        }
    }

    private List<GraphInputRow> SelectedGraphInputRows()
    {
        return _grid.SelectedRows.Cast<DataGridViewRow>()
                    .Select(gridRow => gridRow.DataBoundItem as GraphInputRow)
                    .Where(row => row != null)
                    .ToList();
    }

    private void CommitCurrentEdit()
    {
        if (_grid.IsCurrentCellDirty)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _grid.EndEdit();
    }

    private void SetRows(IEnumerable<GraphInputRow> rows, bool on)
    {
        CommitCurrentEdit();

        // One bulk model change through the owner's batched setter -> one graph recompute.
        List<GraphInputRow> changed = rows.ToList();
        _setPlot(changed.Select(row => row.Node).ToList(), on);

        // Sync the On mirror of every changed row from the model, including rows filtered out of
        // the grid (Clear All passes all rows): RefreshRows only touches currently-bound rows, so
        // a filtered-out row would otherwise show a stale checkmark until the next refresh tick.
        foreach (GraphInputRow row in changed)
            row.Refresh();

        RefreshRows();
    }

    private IEnumerable<GraphInputRow> CurrentRows()
    {
        return _bindingSource.List.Cast<GraphInputRow>();
    }

    private void RefreshRows()
    {
        // The dialog is modal, so Plot only changes from within it; only the rows currently
        // bound to the grid are visible, so refreshing the filtered-out rows every tick is waste.
        // No grid-wide repaint here: each row's PropertyChanged already invalidates exactly the
        // rows whose displayed values changed, so unchanged ticks cost no paint at all.
        foreach (GraphInputRow row in CurrentRows())
            row.Refresh();
    }

    private void RebuildFilter()
    {
        // Filtered-out rows keep the CurrentValue/Unit/Hidden mirrors from when they left the
        // visible set (the per-tick refresh only touches bound rows), so sync every row before
        // matching or a value/unit search would test stale data.
        foreach (GraphInputRow row in _rows)
            row.Refresh();

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

        // Reuse one BindingList and repopulate it in place. Assigning a fresh BindingList per
        // keystroke/toggle would leave the previous list subscribed to every row's PropertyChanged
        // (BindingList hooks INotifyPropertyChanged items and is never unhooked on a DataSource swap).
        // Clear() unhooks the old items and Add() rehooks, so the subscriptions stay balanced.
        _visibleRows.RaiseListChangedEvents = false;
        _visibleRows.Clear();
        foreach (GraphInputRow row in rows)
            _visibleRows.Add(row);
        _visibleRows.RaiseListChangedEvents = true;
        _visibleRows.ResetBindings();
    }

    private sealed class InputsGrid : DataGridView
    {
        /// <summary>
        /// True when the most recent context-menu request came from the keyboard (Apps/Shift+F10).
        /// WM_CONTEXTMENU carries lParam == -1 for keyboard invocation and the packed cursor
        /// position for mouse invocation; it arrives before ContextMenuStrip.Opening, so the flag
        /// and point are always current when the Opening handler reads them.
        /// </summary>
        public bool ContextMenuFromKeyboard { get; private set; }

        /// <summary>Screen coordinates the runtime associated with a mouse-originated menu request.</summary>
        public Point ContextMenuScreenPoint { get; private set; }

        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x007B;
            if (m.Msg == WM_CONTEXTMENU)
            {
                int lParam = unchecked((int)m.LParam.ToInt64());
                ContextMenuFromKeyboard = lParam == -1;
                if (!ContextMenuFromKeyboard)
                {
                    // GET_X_LPARAM / GET_Y_LPARAM: low and high words are signed 16-bit screen
                    // coordinates. Snapshot them here rather than reading the live cursor at
                    // Opening time, which a fast move could shift off the clicked cell.
                    ContextMenuScreenPoint = new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                }
            }

            base.WndProc(ref m);
        }
    }

    private sealed class GraphInputRow : INotifyPropertyChanged
    {
        private readonly Action<SensorNode, bool> _requestPlotChange;
        private bool _on;
        private bool _hidden;
        private string _currentValue;
        private string _unit;

        public GraphInputRow(SensorNode node, Action<SensorNode, bool> requestPlotChange)
        {
            Node = node;
            _requestPlotChange = requestPlotChange;
            SensorPath = BuildSensorPath(node);
            Type = node.Sensor.SensorType.ToString();
            Refresh();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SensorNode Node { get; }

        public bool On
        {
            get { return _on; }
            set
            {
                if (_on == value)
                    return;

                // The row never writes SensorNode.Plot directly: the change is routed through the
                // owner's batched setter, then Refresh syncs the On mirror back from the model.
                _requestPlotChange(Node, value);
                Refresh();
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
            if (_on != Node.Plot)
            {
                _on = Node.Plot;
                OnPropertyChanged(nameof(On));
            }

            Hidden = !Node.IsVisible;

            SplitValueAndUnit(Node.Value, out string currentValue, out string unit);
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
