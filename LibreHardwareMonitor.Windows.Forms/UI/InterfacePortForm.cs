// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public partial class InterfacePortForm : Form
{
    private readonly MainForm _parent;
    private string _localIP;
    private bool _closed;
    
    public InterfacePortForm(MainForm m)
    {
        InitializeComponent();
        _parent = m;
        PopulateNetworkInterfaces(Array.Empty<IPAddress>(), _parent.Server.ListenerIp);
        Shown += InterfacePortForm_Shown;
    }

    private async void InterfacePortForm_Shown(object sender, EventArgs e)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(Dns.GetHostName()).ConfigureAwait(true);
            if (!_closed && !IsDisposed)
                PopulateNetworkInterfaces(addresses, _localIP);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Network interface discovery failed: " + ex.Message);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closed = true;
        Shown -= InterfacePortForm_Shown;
        base.OnFormClosed(e);
    }

    private void PopulateNetworkInterfaces(IEnumerable<IPAddress> addresses, string selectedListenerIp)
    {
        string[] listenerIps = addresses
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Select(ip => ip.ToString())
            .Concat(new[] { selectedListenerIp, "0.0.0.0" })
            .Where(ip => !string.IsNullOrWhiteSpace(ip) && ip != "?")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        interfaceComboBox.BeginUpdate();
        try
        {
            interfaceComboBox.Items.Clear();
            interfaceComboBox.Items.AddRange(listenerIps);

            if (interfaceComboBox.Items.Contains(selectedListenerIp))
                interfaceComboBox.SelectedItem = selectedListenerIp;
            else if (interfaceComboBox.Items.Count > 0)
                interfaceComboBox.SelectedIndex = interfaceComboBox.Items.Count - 1;
        }
        finally
        {
            interfaceComboBox.EndUpdate();
        }

        _localIP = interfaceComboBox.SelectedItem as string ?? "0.0.0.0";
        PortNumericUpDn_ValueChanged(null, EventArgs.Empty);
    }

    private void PortNumericUpDn_ValueChanged(object sender, EventArgs e)
    {
        string url = "http://" + _localIP + ":" + portNumericUpDn.Value + "/";
        webServerLinkLabel.Text = url;
        webServerLinkLabel.Links.Clear();
        webServerLinkLabel.Links.Add(0, webServerLinkLabel.Text.Length, url);
    }

    private void PortOKButton_Click(object sender, EventArgs e)
    {
        _parent.Server.ListenerPort = (int)portNumericUpDn.Value;
        _parent.Server.ListenerIp = _localIP;
        Close();
    }

    private void PortCancelButton_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void PortForm_Load(object sender, EventArgs e)
    {
        interfaceComboBox.SelectedValue = _parent.Server.ListenerIp;
        portNumericUpDn.Value = _parent.Server.ListenerPort;
        PortNumericUpDn_ValueChanged(null, null);
    }

    private void WebServerLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Link.LinkData.ToString()));
        }
        catch { }
    }

    private void PortNumericUpDn_KeyUp(object sender, KeyEventArgs e)
    {
        PortNumericUpDn_ValueChanged(null, null);
    }

    private void interfaceComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {

        _localIP = interfaceComboBox.SelectedItem as string;
        PortNumericUpDn_ValueChanged(null, null);
    }
}
