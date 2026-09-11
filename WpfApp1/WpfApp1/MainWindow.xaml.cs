using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenAI.Chat;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Window1 : Window
    {
        private ChatClient chatClient;
        string apikey = "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";
        public Window1()
        {
            InitializeComponent();
        }
        private void SendQuestion_Click(
        object sender, RoutedEventArgs e)
        {

            string question = txtQuestion.Text;

            if (string.IsNullOrEmpty(question)) return;
            chatClient = new ChatClient(model: "gpt-3.5-tubro", apikey);
            ChatCompletion completion = chatClient.CompleteChat(question);

            txtAnswer.Text =
                "USER:\n" + question +
                "\n\nASSISTANT:\n";

            txtQuestion.Clear();
            txtQuestion.Focus();
        }

    }

}
