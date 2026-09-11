using System.ComponentModel;
using System.Windows.Input;

namespace SimpleChatbot
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string question;
        private string answer;

        public string Question
        {
            get { return question; }
            set
            {
                question = value;
                OnPropertyChanged("Question");
            }
        }

        public string Answer
        {
            get { return answer; }
            set
            {
                answer = value;
                OnPropertyChanged("Answer");
            }
        }

        public ICommand SendCommand { get; }

        public MainWindowViewModel()
        {
            Answer = "ASSISTANT:\n\nHello! I am your chatbot.";
            SendCommand = new RelayCommand(SendQuestion);
        }

        private void SendQuestion()
        {
            if (string.IsNullOrWhiteSpace(Question))
                return;

            Answer = "USER:\n" + Question +
                     "\n\nASSISTANT:\n" +
                     GetAnswer(Question);

            Question = "";
        }

        private string GetAnswer(string text)
        {
            text = text.ToLower();

            if (text.Contains("hello"))
                return "Hello! Nice to meet you.";

            if (text.Contains("c#"))
                return "C# is a programming language.";

            if (text.Contains("revit"))
                return "You can use C# to program Revit API.";

            return "I don't know yet.";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }
    }
}