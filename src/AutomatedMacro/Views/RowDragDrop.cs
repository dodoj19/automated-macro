using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AutomatedMacro;

/// <summary>Drag &amp; drop preslagivanje redaka: ispred, iza ili u grupu.</summary>
public sealed class RowDragDrop
{
    private const double NumberColumn = 58 + 9; // stupac "#" + rub retka

    private readonly ListBox _list;
    private readonly MainViewModel _vm;
    private Point _start;
    private MacroNode? _pressed;
    private MacroNode? _dragging;
    private DropAdorner? _adorner;

    public RowDragDrop(ListBox list, MainViewModel vm)
    {
        _list = list;
        _vm = vm;
        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += OnMouseDown;
        list.PreviewMouseMove += OnMouseMove;
        list.DragOver += OnDragOver;
        list.DragLeave += (_, _) => ClearAdorner();
        list.Drop += OnDrop;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_list);
        var source = e.OriginalSource as DependencyObject;
        _pressed = FindAncestor<ToggleButton>(source) == null && FindAncestor<ScrollBar>(source) == null
            ? FindAncestor<ListBoxItem>(source)?.DataContext as MacroNode
            : null;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed == null || !_vm.IsIdle) return;
        var p = e.GetPosition(_list);
        if (Math.Abs(p.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragging = _pressed;
        _pressed = null;
        try
        {
            DragDrop.DoDragDrop(_list, new DataObject("AutomatedMacro.Node", "node"), DragDropEffects.Move);
        }
        finally
        {
            _dragging = null;
            ClearAdorner();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        AutoScroll(e);

        if (_dragging == null || !TryGetTarget(e, out var target, out var row, out var pos)
            || !_vm.CanDrop(_dragging, target, pos))
        {
            ClearAdorner();
            return;
        }

        e.Effects = DragDropEffects.Move;
        ShowAdorner(row, pos, target.Depth);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var node = _dragging;
        ClearAdorner();
        if (node != null && TryGetTarget(e, out var target, out _, out var pos))
            _vm.MoveNode(node, target, pos);
    }

    private bool TryGetTarget(DragEventArgs e, out MacroNode target, out ListBoxItem row, out DropPosition pos)
    {
        target = null!;
        row = null!;
        pos = DropPosition.After;

        var item = FindAncestor<ListBoxItem>(_list.InputHitTest(e.GetPosition(_list)) as DependencyObject);
        if (item?.DataContext is not MacroNode node)
        {
            // Prazan prostor ispod zadnjeg retka -> na kraj procedure.
            if (_vm.Root.Children.LastOrDefault() is { } last
                && _vm.Rows.LastOrDefault() is { } lastVisible && Container(lastVisible) is { } lastRow)
            {
                target = last;
                row = lastRow;
                pos = DropPosition.After;
                return true;
            }
            return false;
        }

        target = node;
        row = item;
        double y = e.GetPosition(item).Y;
        double h = Math.Max(1, item.ActualHeight);

        if (node is GroupNode g)
        {
            bool open = g.IsExpanded && g.Children.Count > 0;
            if (y < h * 0.28) pos = DropPosition.Before;
            else if (y > h * 0.72 && open && Container(g.Children[0]) is { } firstRow)
            {
                // Donji rub otvorene grupe = na sam vrh grupe.
                target = g.Children[0];
                row = firstRow;
                pos = DropPosition.Before;
            }
            else if (y > h * 0.72 && !open) pos = DropPosition.After;
            else pos = DropPosition.Into;
        }
        else
        {
            pos = y < h / 2 ? DropPosition.Before : DropPosition.After;
        }
        return true;
    }

    private ListBoxItem? Container(MacroNode node) => _list.ItemContainerGenerator.ContainerFromItem(node) as ListBoxItem;

    private void AutoScroll(DragEventArgs e)
    {
        var sv = FindDescendant<ScrollViewer>(_list);
        if (sv == null) return;
        double y = e.GetPosition(_list).Y;
        const double edge = 30;
        if (y < edge) sv.ScrollToVerticalOffset(sv.VerticalOffset - 14);
        else if (y > _list.ActualHeight - edge) sv.ScrollToVerticalOffset(sv.VerticalOffset + 14);
    }

    private void ShowAdorner(ListBoxItem row, DropPosition pos, int depth)
    {
        double indent = NumberColumn + depth * GuideLines.Indent;
        if (_adorner != null && ReferenceEquals(_adorner.AdornedElement, row))
        {
            _adorner.Update(pos, indent);
            return;
        }
        ClearAdorner();
        var layer = AdornerLayer.GetAdornerLayer(row);
        if (layer == null) return;
        _adorner = new DropAdorner(row, pos, indent);
        layer.Add(_adorner);
    }

    private void ClearAdorner()
    {
        if (_adorner == null) return;
        AdornerLayer.GetAdornerLayer(_adorner.AdornedElement)?.Remove(_adorner);
        _adorner = null;
    }

    public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    public static T? FindDescendant<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is T t) return t;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}

/// <summary>Linija (ispred/iza) ili okvir (u grupu) koji pokazuje gdje ce redak pasti.</summary>
public sealed class DropAdorner : Adorner
{
    private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)));
    private static readonly Brush AccentFill = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0x7C, 0x5C, 0xFF)));
    private static readonly Pen Line = FreezePen(new Pen(Accent, 2));

    private DropPosition _position;
    private double _indent;

    public DropAdorner(UIElement adorned, DropPosition position, double indent) : base(adorned)
    {
        _position = position;
        _indent = indent;
        IsHitTestVisible = false;
    }

    public void Update(DropPosition position, double indent)
    {
        if (_position == position && _indent == indent) return;
        _position = position;
        _indent = indent;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = AdornedElement.RenderSize;
        double right = size.Width - 10;
        switch (_position)
        {
            case DropPosition.Into:
                dc.DrawRoundedRectangle(AccentFill, Line, new Rect(9, 1, size.Width - 18, size.Height - 2), 6, 6);
                break;
            case DropPosition.Before:
                dc.DrawLine(Line, new Point(_indent, 1), new Point(right, 1));
                dc.DrawEllipse(Accent, null, new Point(_indent, 1), 4, 4);
                break;
            case DropPosition.After:
                dc.DrawLine(Line, new Point(_indent, size.Height - 1), new Point(right, size.Height - 1));
                dc.DrawEllipse(Accent, null, new Point(_indent, size.Height - 1), 4, 4);
                break;
        }
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
}
