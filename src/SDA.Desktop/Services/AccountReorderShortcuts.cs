namespace SDA.Desktop.Services
{
    public enum AccountMoveDirection
    {
        Up,
        Down
    }

    public static class AccountReorderShortcuts
    {
        public static bool TryResolve(bool control, bool command, bool up, bool down, out AccountMoveDirection direction)
        {
            direction = AccountMoveDirection.Up;
            if (!control && !command)
            {
                return false;
            }

            if (up && !down)
            {
                direction = AccountMoveDirection.Up;
                return true;
            }

            if (down && !up)
            {
                direction = AccountMoveDirection.Down;
                return true;
            }

            return false;
        }
    }

    public static class AccountSearchKeyboard
    {
        public static bool ShouldFocusSearch(bool textInputAlreadyFocused, bool commandOrControl, char character)
        {
            if (textInputAlreadyFocused || commandOrControl)
            {
                return false;
            }

            return char.IsLetterOrDigit(character);
        }
    }
}
