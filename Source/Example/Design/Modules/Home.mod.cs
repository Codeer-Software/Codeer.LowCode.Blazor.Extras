
void カレンダーテストボタン_OnClick()
{
    using var x = this.SuspendNotifyStateChanged();
    Calendar.ViewMode = CalendarViewMode.Day;
    Calendar.SelectedDate = new DateTime(2027, 4, 3);
}
void DetailLayoutDesign_OnAfterInitialization()
{
    RichText.Value = "abc\r\nefg";
//    Gantt.IsEnabled = false;
}