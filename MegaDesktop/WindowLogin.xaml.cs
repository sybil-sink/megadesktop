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
using MegaApi;

namespace MegaWpf
{
    /// <summary>
    /// Interaction logic for WindowLogin.xaml
    /// </summary>
    public partial class WindowLogin : Window
    {
        public event EventHandler<SuccessfulLoginArgs> OnLoggedIn;
        public WindowLogin()
        {
            InitializeComponent();
            textBoxPass_LostFocus(null, null);
            textBoxEmail_LostFocus(null, null);
        }

        private void textBoxPass_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(textBoxPass.Password))
            {
                textBoxPass.Password = "password";
            }
        }

        private void textBoxEmail_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(textBoxEmail.Text))
            {
                textBoxEmail.Text = "Email";
            }
        }

        private void textBoxEmail_GotFocus(object sender, RoutedEventArgs e)
        {
            if (textBoxEmail.Text == "Email")
            {
                textBoxEmail.Text = "";
            }
        }

        private void textBoxPass_GotFocus(object sender, RoutedEventArgs e)
        {
            if (textBoxPass.Password == "password")
            {
                textBoxPass.Password = "";
            }
        }

        private void buttonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void buttonOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(textBoxPass.Password) ||
               string.IsNullOrEmpty(textBoxEmail.Text))
            { return; }

            textBoxStatus.Text = "Checking...";
            Mega.Init(new MegaUser(textBoxEmail.Text, textBoxPass.Password),
                (m) => 
                {
                    if (OnLoggedIn != null)
                    {
                        OnLoggedIn(this, new SuccessfulLoginArgs { Api = m});
                    }
                },
                (err) => { Invoke(()=> textBoxStatus.Text = "Incorrect login or password"); });
        }
        void Invoke(Action fn)
        {
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, (Delegate)fn);
        }
    }
    
    public class SuccessfulLoginArgs : EventArgs
    {
        public Mega Api { get; set; }
    }
}
