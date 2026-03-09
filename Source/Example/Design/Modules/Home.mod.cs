
void カレンダーテストボタン_OnClick()
{
    using var x = this.SuspendNotifyStateChanged();
    Calendar.ViewMode = CalendarViewMode.Day;
    Calendar.SelectedDate = new DateTime(2027, 4, 3);
}