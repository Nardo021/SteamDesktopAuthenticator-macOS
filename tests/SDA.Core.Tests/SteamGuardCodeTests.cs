using SteamAuth;
using System;
using Xunit;

namespace SDA.Core.Tests
{
    public class SteamGuardCodeTests
    {
        // Base64 of the bytes "synthetic-shared-secret". Not a real authenticator secret.
        private const string SyntheticSharedSecret = "c3ludGhldGljLXNoYXJlZC1zZWNyZXQ=";

        // Codes below were produced independently with HMAC-SHA1 and the Steam alphabet.
        // 1600000000 and 1600000019 fall in the same 30-second window.
        private const long FirstWindow = 1600000000;
        private const long FirstWindowEnd = 1600000019;
        private const long NextWindow = 1600000020;
        private const string FirstWindowCode = "PFRRH";
        private const string NextWindowCode = "VWFNV";
        private const string FixedTimestampCode = "7JGB8";

        [Fact]
        public void GenerateSteamGuardCodeForTime_IsDeterministic()
        {
            SteamGuardAccount account = CreateAccount();

            string first = account.GenerateSteamGuardCodeForTime(FirstWindow);
            string second = account.GenerateSteamGuardCodeForTime(FirstWindow);

            Assert.Equal(FirstWindowCode, first);
            Assert.Equal(first, second);
        }

        [Fact]
        public void GenerateSteamGuardCodeForTime_UsesFiveCharacterSteamAlphabet()
        {
            string code = CreateAccount().GenerateSteamGuardCodeForTime(1234567890);

            Assert.Equal(FixedTimestampCode, code);
            Assert.Equal(5, code.Length);
            Assert.Matches("^[23456789BCDFGHJKMNPQRTVWXY]{5}$", code);
        }

        [Fact]
        public void AdjacentTimeWindows_ShareACodeOnlyInsideTheSameWindow()
        {
            SteamGuardAccount account = CreateAccount();

            Assert.Equal(FirstWindowCode, account.GenerateSteamGuardCodeForTime(FirstWindow));
            Assert.Equal(FirstWindowCode, account.GenerateSteamGuardCodeForTime(FirstWindowEnd));
            Assert.Equal(NextWindowCode, account.GenerateSteamGuardCodeForTime(NextWindow));
            Assert.NotEqual(FirstWindowCode, NextWindowCode);
        }

        [Fact]
        public void EmptySharedSecret_ReturnsEmptyString()
        {
            var account = new SteamGuardAccount();

            Assert.Equal("", account.GenerateSteamGuardCodeForTime(FirstWindow));

            account.SharedSecret = "";
            Assert.Equal("", account.GenerateSteamGuardCodeForTime(FirstWindow));
        }

        [Fact]
        public void InvalidSharedSecret_ThrowsFormatException()
        {
            var account = new SteamGuardAccount
            {
                SharedSecret = "@@not-base64@@"
            };

            Assert.Throws<FormatException>(() => account.GenerateSteamGuardCodeForTime(FirstWindow));
        }

        private static SteamGuardAccount CreateAccount()
        {
            return new SteamGuardAccount
            {
                SharedSecret = SyntheticSharedSecret
            };
        }
    }
}
