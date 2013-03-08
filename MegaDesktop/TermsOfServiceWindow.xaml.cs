using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MegaDesktop
{
    /// <summary>
    /// Interaction logic for TermsOfServiceWindow.xaml
    /// </summary>
    public partial class TermsOfServiceWindow : Window
    {
        public TermsOfServiceWindow()
        {
            InitializeComponent();
        }

        private void Grid_Loaded_1(object sender, RoutedEventArgs e)
        {
            TosBrowser.Navigate(new Uri("http://g.static.mega.co.nz/pages/terms.html"));
            AcceptTos.Focus();
        }

        private void AcceptTos_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void DeclineTos_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
