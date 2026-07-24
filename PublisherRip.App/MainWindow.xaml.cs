using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using PublisherRip.App.Models;
using PublisherRip.App.Services;

namespace PublisherRip.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DropImportService _dropImportService = new();
    private string _statusText = "Drop images, PDFs, or Outlook attachments to build a print queue.";
    private PrintPageItem? _selectedPage;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        Pages.CollectionChanged += PagesCollectionChanged;
        RefreshUiState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PrintPageItem> Pages { get; } = new();

    public PrintPageItem? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                RefreshUiState();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasPages => Pages.Count > 0;

    public bool ShowEmptyState => !HasPages;

    public bool HasSelection => SelectedPage is not null;

    public bool CanMoveUp => !IsBusy && SelectedPage is not null && Pages.IndexOf(SelectedPage) > 0;

    public bool CanMoveDown => !IsBusy && SelectedPage is not null && Pages.IndexOf(SelectedPage) < Pages.Count - 1;

    public bool CanPrint => !IsBusy && HasPages;

    public string PageCountLabel => Pages.Count == 1 ? "1 page queued" : $"{Pages.Count} pages queued";

    private bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshUiState();
            }
        }
    }

    private async void HandlePreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!DropImportService.CanImport(e.Data))
        {
            StatusText = "That drop did not contain image, PDF, or Outlook attachment data I can use.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Importing dropped files...";

            var result = await _dropImportService.ImportAsync(e.Data);

            foreach (var page in result.Pages)
            {
                Pages.Add(page);
            }

            if (result.Pages.Count > 0)
            {
                SelectedPage ??= result.Pages[0];
                StatusText = result.Pages.Count == 1
                    ? "Added 1 printable page."
                    : $"Added {result.Pages.Count} printable pages.";
            }
            else
            {
                StatusText = "I could not extract any printable pages from that drop.";
            }

            if (result.Warnings.Count > 0)
            {
                var warningText = string.Join(Environment.NewLine, result.Warnings.Take(10));
                MessageBox.Show(
                    this,
                    warningText,
                    "Some items could not be added",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = "Import failed.";
            MessageBox.Show(
                this,
                $"I couldn't read that drop.\n\n{ex.Message}",
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandlePreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropImportService.CanImport(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedPage is null)
        {
            return;
        }

        var index = Pages.IndexOf(SelectedPage);
        if (index <= 0)
        {
            return;
        }

        Pages.Move(index, index - 1);
        StatusText = $"Moved {SelectedPage.DisplayName} up.";
        RefreshUiState();
    }

    private void MoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedPage is null)
        {
            return;
        }

        var index = Pages.IndexOf(SelectedPage);
        if (index < 0 || index >= Pages.Count - 1)
        {
            return;
        }

        Pages.Move(index, index + 1);
        StatusText = $"Moved {SelectedPage.DisplayName} down.";
        RefreshUiState();
    }

    private void RemoveClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedPage is null)
        {
            return;
        }

        var removedName = SelectedPage.DisplayName;
        var index = Pages.IndexOf(SelectedPage);
        Pages.Remove(SelectedPage);

        if (Pages.Count == 0)
        {
            SelectedPage = null;
            StatusText = "Queue cleared.";
        }
        else
        {
            var nextIndex = Math.Min(index, Pages.Count - 1);
            SelectedPage = Pages[nextIndex];
            StatusText = $"Removed {removedName}.";
        }

        RefreshUiState();
    }

    private void ClearAllClicked(object sender, RoutedEventArgs e)
    {
        Pages.Clear();
        SelectedPage = null;
        StatusText = "Queue cleared.";
        RefreshUiState();
    }

    private void PrintClicked(object sender, RoutedEventArgs e)
    {
        if (!HasPages)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var printDialog = new System.Windows.Controls.PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                StatusText = "Print cancelled.";
                return;
            }

            var capabilities = printDialog.PrintQueue?.GetPrintCapabilities(printDialog.PrintTicket);
            var imageableArea = capabilities?.PageImageableArea;

            var pageSize = imageableArea is null
                ? new Size(
                    Math.Max(printDialog.PrintableAreaWidth, 1),
                    Math.Max(printDialog.PrintableAreaHeight, 1))
                : new Size(
                    Math.Max((imageableArea.OriginWidth * 2) + imageableArea.ExtentWidth, 1),
                    Math.Max((imageableArea.OriginHeight * 2) + imageableArea.ExtentHeight, 1));

            var contentRect = imageableArea is null
                ? new Rect(0, 0, Math.Max(pageSize.Width, 1), Math.Max(pageSize.Height, 1))
                : new Rect(
                    imageableArea.OriginWidth,
                    imageableArea.OriginHeight,
                    Math.Max(imageableArea.ExtentWidth, 1),
                    Math.Max(imageableArea.ExtentHeight, 1));

            var paginator = new PrintPagePaginator(Pages.ToList(), pageSize, contentRect);
            printDialog.PrintDocument(paginator, $"Publisher RIP ({Pages.Count} pages)");
            StatusText = Pages.Count == 1 ? "Sent 1 page to the printer." : $"Sent {Pages.Count} pages to the printer.";
        }
        catch (Exception ex)
        {
            StatusText = "Printing failed.";
            MessageBox.Show(
                this,
                $"I couldn't print the current queue.\n\n{ex.Message}",
                "Printing failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshUiState();
    }

    private void RefreshUiState()
    {
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanPrint));
        OnPropertyChanged(nameof(PageCountLabel));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
