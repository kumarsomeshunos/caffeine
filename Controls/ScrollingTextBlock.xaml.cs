using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CaffeineWin.Controls;

public partial class ScrollingTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ScrollingTextBlock),
            new PropertyMetadata("", OnTextChanged));

    public static readonly DependencyProperty TextFontSizeProperty =
        DependencyProperty.Register(nameof(TextFontSize), typeof(double), typeof(ScrollingTextBlock),
            new PropertyMetadata(14.0, OnFontPropertyChanged));

    public static readonly DependencyProperty TextFontWeightProperty =
        DependencyProperty.Register(nameof(TextFontWeight), typeof(FontWeight), typeof(ScrollingTextBlock),
            new PropertyMetadata(FontWeights.Normal, OnFontPropertyChanged));

    public static readonly DependencyProperty TextForegroundProperty =
        DependencyProperty.Register(nameof(TextForeground), typeof(Brush), typeof(ScrollingTextBlock),
            new PropertyMetadata(Brushes.Black, OnFontPropertyChanged));

    public static readonly DependencyProperty TextFontFamilyProperty =
        DependencyProperty.Register(nameof(TextFontFamily), typeof(FontFamily), typeof(ScrollingTextBlock),
            new PropertyMetadata(new FontFamily("Segoe UI"), OnFontPropertyChanged));

    public static readonly DependencyProperty AnimationMillisecondsProperty =
        DependencyProperty.Register(nameof(AnimationMilliseconds), typeof(int), typeof(ScrollingTextBlock),
            new PropertyMetadata(180));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double TextFontSize { get => (double)GetValue(TextFontSizeProperty); set => SetValue(TextFontSizeProperty, value); }
    public FontWeight TextFontWeight { get => (FontWeight)GetValue(TextFontWeightProperty); set => SetValue(TextFontWeightProperty, value); }
    public Brush TextForeground { get => (Brush)GetValue(TextForegroundProperty); set => SetValue(TextForegroundProperty, value); }
    public FontFamily TextFontFamily { get => (FontFamily)GetValue(TextFontFamilyProperty); set => SetValue(TextFontFamilyProperty, value); }
    public int AnimationMilliseconds { get => (int)GetValue(AnimationMillisecondsProperty); set => SetValue(AnimationMillisecondsProperty, value); }

    private readonly List<CharSlot> _slots = new();
    private string _currentText = "";

    public ScrollingTextBlock()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScrollingTextBlock)d;
        var newText = (string)e.NewValue ?? "";
        var oldText = control._currentText;

        if (oldText == newText) return;

        if (!control.IsLoaded)
        {
            control._currentText = newText;
            control.RebuildSlots(newText, false);
            return;
        }

        control.TransitionTo(oldText, newText);
        control._currentText = newText;
    }

    private static void OnFontPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScrollingTextBlock)d;
        control.ApplyFontToAllSlots();
    }

    private void TransitionTo(string oldText, string newText)
    {
        if (oldText.Length != newText.Length)
        {
            RebuildSlots(newText, true);
            return;
        }

        for (int i = 0; i < newText.Length; i++)
        {
            if (oldText[i] != newText[i])
                _slots[i].AnimateToChar(newText[i], AnimationMilliseconds);
        }
    }

    private void RebuildSlots(string text, bool animateNew)
    {
        var oldText = _currentText;

        while (_slots.Count > text.Length)
        {
            CharPanel.Children.RemoveAt(CharPanel.Children.Count - 1);
            _slots.RemoveAt(_slots.Count - 1);
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (i < _slots.Count)
            {
                if (animateNew && i < oldText.Length && oldText[i] != text[i])
                    _slots[i].AnimateToChar(text[i], AnimationMilliseconds);
                else
                    _slots[i].SetChar(text[i]);
            }
            else
            {
                var slot = new CharSlot(text[i], TextFontSize, TextFontWeight, TextForeground, TextFontFamily);
                _slots.Add(slot);
                CharPanel.Children.Add(slot.Container);
            }
        }
    }

    private void ApplyFontToAllSlots()
    {
        foreach (var slot in _slots)
            slot.ApplyFont(TextFontSize, TextFontWeight, TextForeground, TextFontFamily);
    }

    private class CharSlot
    {
        public Grid Container { get; }
        private readonly TextBlock _display;
        private readonly TextBlock _incoming;
        private readonly TranslateTransform _displayTranslate;
        private readonly TranslateTransform _incomingTranslate;
        private bool _isAnimating;

        public CharSlot(char c, double fontSize, FontWeight fontWeight, Brush foreground, FontFamily fontFamily)
        {
            _displayTranslate = new TranslateTransform(0, 0);
            _incomingTranslate = new TranslateTransform(0, 0);

            _display = new TextBlock
            {
                Text = c.ToString(),
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = foreground,
                FontFamily = fontFamily,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _displayTranslate
            };

            _incoming = new TextBlock
            {
                Text = "",
                Opacity = 0,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = foreground,
                FontFamily = fontFamily,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _incomingTranslate
            };

            Container = new Grid { ClipToBounds = true };
            Container.Children.Add(_display);
            Container.Children.Add(_incoming);

            SetMinWidth(c, fontSize, fontFamily);
        }

        public void SetChar(char c)
        {
            _display.Text = c.ToString();
            _displayTranslate.Y = 0;
            _incoming.Opacity = 0;
            SetMinWidth(c, _display.FontSize, _display.FontFamily);
        }

        public void AnimateToChar(char c, int ms)
        {
            if (_isAnimating)
            {
                _displayTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                _incomingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                _display.Text = _incoming.Text.Length > 0 ? _incoming.Text : _display.Text;
                _displayTranslate.Y = 0;
                _incoming.Opacity = 0;
                _incomingTranslate.Y = 0;
                _isAnimating = false;
            }

            _isAnimating = true;
            var height = _display.ActualHeight;
            if (height <= 0)
                height = _display.FontSize * 1.4;

            _displayTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _incomingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _displayTranslate.Y = 0;
            _incomingTranslate.Y = height;

            _incoming.Text = c.ToString();
            _incoming.Opacity = 1;

            var duration = new Duration(TimeSpan.FromMilliseconds(ms));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

            var slideOut = new DoubleAnimation(0, -height, duration) { EasingFunction = ease };
            var slideIn = new DoubleAnimation(height, 0, duration) { EasingFunction = ease };

            slideIn.Completed += (_, _) =>
            {
                _displayTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                _incomingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                _display.Text = c.ToString();
                _displayTranslate.Y = 0;
                _incoming.Opacity = 0;
                _incomingTranslate.Y = 0;
                _isAnimating = false;
                SetMinWidth(c, _display.FontSize, _display.FontFamily);
            };

            _displayTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
            _incomingTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        }

        public void ApplyFont(double fontSize, FontWeight fontWeight, Brush foreground, FontFamily fontFamily)
        {
            _display.FontSize = fontSize;
            _display.FontWeight = fontWeight;
            _display.Foreground = foreground;
            _display.FontFamily = fontFamily;
            _incoming.FontSize = fontSize;
            _incoming.FontWeight = fontWeight;
            _incoming.Foreground = foreground;
            _incoming.FontFamily = fontFamily;

            if (_display.Text.Length > 0)
                SetMinWidth(_display.Text[0], fontSize, fontFamily);
        }

        private void SetMinWidth(char c, double fontSize, FontFamily fontFamily)
        {
            if (char.IsDigit(c))
                Container.MinWidth = fontSize * 0.62;
            else if (c == ':')
                Container.MinWidth = fontSize * 0.35;
            else
                Container.MinWidth = 0;
        }
    }
}
