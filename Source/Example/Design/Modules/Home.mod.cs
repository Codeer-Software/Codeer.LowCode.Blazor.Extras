void DetailLayoutDesign_OnAfterInitialization()
{
    RichText.Value = "abc\r\nefg";
}

void カレンダーテストボタン_OnClick()
{
    using var x = this.SuspendNotifyStateChanged();
    Calendar.ViewMode = CalendarViewMode.Day;
    Calendar.SelectedDate = new DateTime(2027, 4, 3);
}

void ガントテストボタン_OnClick()
{    
    using var x = this.SuspendNotifyStateChanged();
    Gantt.ViewMode = GanttViewMode.Month;
    Gantt.ViewStart = new DateTime(2027, 4, 3);
}

void リッチテキストテスト_OnClick()
{
    RichText.IsEnabled = !RichText.IsEnabled;
    テキスト.IsEnabled = !テキスト.IsEnabled;
}