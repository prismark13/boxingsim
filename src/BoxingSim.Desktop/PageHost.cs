using System.Windows;
using System.Windows.Controls;

namespace BoxingSim.Desktop;

/// <summary>A content page in the shell: a heading, a line of context, and a scrolling body in a card. Every list
/// page shares it, so they can't drift apart in spacing or type.</summary>
public sealed class PageHost : ContentControl
{
    static PageHost() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHost), new FrameworkPropertyMetadata(typeof(PageHost)));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHost), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHost), new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
