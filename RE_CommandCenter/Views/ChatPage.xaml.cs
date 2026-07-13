using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using RE_CommandCenter.ViewModels;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace RE_CommandCenter.Views
{
    public sealed partial class ChatPage : Page
    {
        public ChatViewModel ViewModel { get; }

        public ChatPage()
        {
            InitializeComponent();

            // Noi UI voi ViewModel:
            // XAML binding doc ChatHistory/UserInput va goi SendMessageCommand tu object nay.
            ViewModel = new ChatViewModel();
            DataContext = ViewModel;

            // Nap prompt config ngay khi mo page.
            // Giai doan 1 dung IDENTITY.md + ROUTING_RULES.yaml lam system prompt cho Ollama.
            _ = ViewModel.InitializeAsync();

            // Auto-scroll khi co tin nhan moi
            ViewModel.ChatHistory.CollectionChanged += ChatHistory_CollectionChanged;
        }

        // ======================================================================================================
        // [QUY TẮC "RANH GIỚI" CHO AI] - GIẢI THÍCH LUỒNG LOGIC (CẬP NHẬT THEO TƯ DUY CHIEF ARCHITECT)
        // ======================================================================================================
        // HÀM: ChatHistory_CollectionChanged
        // MỤC ĐÍCH: Lắng nghe sự thay đổi của danh sách tin nhắn (ChatHistory).
        // LUỒNG HOẠT ĐỘNG (Black Box):
        //   - Input: Nhận tín hiệu khi có 1 Bong bóng Chat mới (Add) được sinh ra.
        //   - Xử lý: 
        //       1. Ép giao diện (ScrollViewer) phải trượt xuống đáy ngay lập tức để nhìn thấy bong bóng mới.
        //       2. "Bắt cóc" cái bong bóng mới này và gắn thêm một "máy nghe lén" (Message_PropertyChanged) 
        //          để theo dõi xem chữ bên trong bong bóng có đang chạy dài ra hay không (Streaming).
        //   - Output: Giao diện luôn tự động focus xuống tin nhắn mới nhất, không bắt người dùng phải cuộn tay.
        // ======================================================================================================
        private void ChatHistory_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Scroll to bottom whenever a new message is added
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                // Update layout before scrolling to ensure the new item's size is calculated
                DispatcherQueue.TryEnqueue(() =>
                {
                    ChatScrollViewer.UpdateLayout();
                    ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
                });

                // Lang nghe su kien khi text (Content) tang len do streaming de scroll theo
                foreach (var item in e.NewItems)
                {
                    if (item is ChatMessage message)
                    {
                        message.PropertyChanged -= Message_PropertyChanged;
                        message.PropertyChanged += Message_PropertyChanged;
                    }
                }
            }
        }

        // ======================================================================================================
        // HÀM: Message_PropertyChanged
        // MỤC ĐÍCH: Bám theo chữ (Text Streaming) của AI để cuộn màn hình.
        // LUỒNG HOẠT ĐỘNG (Black Box):
        //   - Input: Nhận tín hiệu mỗi khi thuộc tính "Content" của bong bóng AI thay đổi (chữ đang chạy).
        //   - Xử lý: Đẩy lệnh cuộn (ChangeView) vào hàng đợi (DispatcherQueue) để tránh đụng độ luồng UI.
        //   - Output: Bong bóng AI dài ra đến đâu, thanh cuộn tự động bám đuôi theo đến đó một cách mượt mà.
        // ======================================================================================================
        private void Message_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Tu dong scroll xuong khi chu dang chay (streaming)
            if (e.PropertyName == nameof(ChatMessage.Content))
            {
                // Phai day qua Dispatcher vi streaming the chay o thread khac (mac du ObservableProperty thuong tu dispatch)
                DispatcherQueue.TryEnqueue(() =>
                {
                    ChatScrollViewer.UpdateLayout();
                    ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
                });
            }
        }

        private void UserInputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Enter la duong tat cho nut Send.
            // Logic gui message van nam trong ViewModel de UI khong bi tron voi orchestration.
            if (e.Key == VirtualKey.Enter)
            {
                if (ViewModel.SendMessageCommand.CanExecute(null))
                {
                    ViewModel.SendMessageCommand.Execute(null);
                }

                e.Handled = true;
            }
        }

        private void CopyMessageButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Hop den copy message:
            // Input la Content cua chat bubble, output la clipboard cua Windows.
            // Dung khi can copy lai request/response JSON de doi chieu voi tool output.
            if (sender is Button button && button.Tag is string content)
            {
                var package = new DataPackage();
                package.SetText(content);
                Clipboard.SetContent(package);
                _ = ShowCopyToastAsync();
            }
        }

        private async Task ShowCopyToastAsync()
        {
            // Floating feedback nho sau khi copy.
            // Khong dung dialog modal de khong cat ngang luong test tool trong chat.
            CopyToast.Opacity = 1;

            await Task.Delay(TimeSpan.FromMilliseconds(900));

            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(animation, CopyToast);
            Storyboard.SetTargetProperty(animation, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
    }
}
