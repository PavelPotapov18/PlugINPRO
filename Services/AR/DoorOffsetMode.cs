namespace PlugINPRO.Services.AR
{
    public enum DoorOffsetMode
    {
        None,           // Не заводить контур в дверные проемы
        ToWallCenter,   // До середины стены
        ToWallFull,     // На всю толщину стены
        Custom          // На указанное значение (мм)
    }
}