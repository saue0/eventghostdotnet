using System.Collections.ObjectModel;

namespace EventGhostDotNet;

internal sealed class ConfigurationItemCollection<T>(ConfigurationContainer owner) : ObservableCollection<T>
    where T : ConfigurationItem
{
    protected override void InsertItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Parent is not null)
            throw new InvalidOperationException("Cet élément appartient déjà à un conteneur.");
        if (index < 0 || index > Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        CheckReentrancy();
        item.Parent = owner;
        try
        {
            base.InsertItem(index, item);
        }
        finally
        {
            if (!Items.Contains(item) && ReferenceEquals(item.Parent, owner))
                item.Parent = null;
        }
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];
        CheckReentrancy();
        item.Parent = null;
        try
        {
            base.RemoveItem(index);
        }
        finally
        {
            if (Items.Contains(item))
                item.Parent = owner;
        }
    }

    protected override void SetItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var previous = this[index];
        if (ReferenceEquals(previous, item))
        {
            base.SetItem(index, item);
            return;
        }
        if (item.Parent is not null)
            throw new InvalidOperationException("Cet élément appartient déjà à un conteneur.");
        CheckReentrancy();
        previous.Parent = null;
        item.Parent = owner;
        try
        {
            base.SetItem(index, item);
        }
        finally
        {
            if (Items.Contains(previous))
                previous.Parent = owner;
            if (!Items.Contains(item) && ReferenceEquals(item.Parent, owner))
                item.Parent = null;
        }
    }

    protected override void ClearItems()
    {
        CheckReentrancy();
        var previous = this.ToArray();
        foreach (var item in previous)
            item.Parent = null;
        try
        {
            base.ClearItems();
        }
        finally
        {
            foreach (var item in previous)
                if (Items.Contains(item))
                    item.Parent = owner;
        }
    }
}
