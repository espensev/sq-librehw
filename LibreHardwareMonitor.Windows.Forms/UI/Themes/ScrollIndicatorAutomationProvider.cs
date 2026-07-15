using System;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.UI.Themes
{
    internal sealed class ScrollIndicatorAutomationProvider : IRawElementProviderSimple, IRangeValueProvider
    {
        private readonly Control _owner;
        private readonly ScrollBar _scrollbar;
        private readonly Orientation _orientation;

        internal ScrollIndicatorAutomationProvider(Control owner, ScrollBar scrollbar, Orientation orientation)
        {
            _owner = owner;
            _scrollbar = scrollbar;
            _orientation = orientation;
        }

        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

        public IRawElementProviderSimple HostRawElementProvider => Read(
            () => AutomationInteropProvider.HostProviderFromHandle(_owner.Handle));

        public object GetPatternProvider(int patternId)
        {
            return patternId == RangeValuePatternIdentifiers.Pattern.Id ? this : null;
        }

        public object GetPropertyValue(int propertyId)
        {
            if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
                return ControlType.ScrollBar.Id;

            if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
                return Read(() => _scrollbar.AccessibleName ?? string.Empty);

            if (propertyId == AutomationElementIdentifiers.AutomationIdProperty.Id)
                return Read(() => _owner.Name ?? string.Empty);

            if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id ||
                propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id ||
                propertyId == AutomationElementIdentifiers.IsRangeValuePatternAvailableProperty.Id)
            {
                return true;
            }

            if (propertyId == AutomationElementIdentifiers.IsEnabledProperty.Id)
                return Read(() => _owner.Enabled && _scrollbar.Enabled);

            if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id ||
                propertyId == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id)
            {
                return false;
            }

            if (propertyId == AutomationElementIdentifiers.OrientationProperty.Id)
            {
                return _orientation == Orientation.Vertical
                    ? OrientationType.Vertical
                    : OrientationType.Horizontal;
            }

            return null;
        }

        public bool IsReadOnly => Read(() => !_owner.Enabled || !_scrollbar.Enabled);

        public double LargeChange => Read(() => (double)_scrollbar.LargeChange);

        public double Maximum => Read(() => (double)ScrollIndicatorGeometry.GetEffectiveMaximum(_scrollbar));

        public double Minimum => Read(() => (double)_scrollbar.Minimum);

        public double SmallChange => Read(() => (double)_scrollbar.SmallChange);

        public double Value => Read(() => (double)_scrollbar.Value);

        public void SetValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("A scrollbar value must be a finite number.", nameof(value));

            Write(() =>
            {
                if (!_owner.Enabled || !_scrollbar.Enabled)
                    throw new ElementNotEnabledException();

                int minimum = _scrollbar.Minimum;
                int maximum = ScrollIndicatorGeometry.GetEffectiveMaximum(_scrollbar);
                if (value < minimum || value > maximum)
                    throw new ArgumentOutOfRangeException(nameof(value));

                int roundedValue = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                _scrollbar.Value = Math.Max(minimum, Math.Min(maximum, roundedValue));
            });
        }

        private T Read<T>(Func<T> action)
        {
            EnsureAvailable();
            return _owner.InvokeRequired ? (T)_owner.Invoke(action) : action();
        }

        private void Write(Action action)
        {
            EnsureAvailable();
            if (_owner.InvokeRequired)
                _owner.Invoke(action);
            else
                action();
        }

        private void EnsureAvailable()
        {
            if (_owner.IsDisposed || !_owner.IsHandleCreated || _scrollbar.IsDisposed)
                throw new ElementNotAvailableException();
        }
    }
}
