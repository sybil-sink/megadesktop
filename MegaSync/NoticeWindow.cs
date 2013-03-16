using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MegaSync
{
    public partial class NoticeWindow : Form
    {
        public NoticeWindow()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult = checkBox1.Checked ? DialogResult.OK : System.Windows.Forms.DialogResult.Cancel;
            Close();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://mega.co.nz/#terms");
        }
    }
}
