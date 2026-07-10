using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class SmtpSettingsTests
    {
        [Fact]
        public void Password_round_trips_but_is_not_stored_in_clear()
        {
            var s = new SmtpSettings { Password = "s3cret-app-password" };

            Assert.Equal("s3cret-app-password", s.Password);              // decrypts back
            Assert.NotEqual("s3cret-app-password", s.PasswordProtected);  // not persisted in clear
            Assert.NotEmpty(s.PasswordProtected);
        }

        [Fact]
        public void ClientSecret_round_trips()
        {
            var s = new SmtpSettings { ClientSecret = "oauth-secret" };
            Assert.Equal("oauth-secret", s.ClientSecret);
            Assert.NotEqual("oauth-secret", s.ClientSecretProtected);
        }

        [Fact]
        public void Disabled_is_not_configured()
        {
            var s = new SmtpSettings { Enabled = false };
            Assert.False(s.IsConfigured(out _));
        }

        [Fact]
        public void Basic_auth_requires_username_and_password()
        {
            var s = new SmtpSettings
            {
                Enabled = true,
                Host = "smtp.office365.com",
                FromAddress = "noreply@acme.example",
                AuthMode = SmtpAuthMode.BasicOrAppPassword
            };
            Assert.False(s.IsConfigured(out _)); // no creds yet

            s.Username = "noreply@acme.example";
            s.Password = "app-password";
            Assert.True(s.IsConfigured(out var err));
            Assert.Equal(string.Empty, err);
        }

        [Fact]
        public void OAuth2_requires_tenant_client_and_secret()
        {
            var s = new SmtpSettings
            {
                Enabled = true,
                Host = "smtp.office365.com",
                FromAddress = "noreply@acme.example",
                AuthMode = SmtpAuthMode.OAuth2
            };
            Assert.False(s.IsConfigured(out _));

            s.TenantId = "tenant";
            s.ClientIdOAuth = "client";
            s.ClientSecret = "secret";
            Assert.True(s.IsConfigured(out _));
        }

        [Fact]
        public void Defaults_target_office365()
        {
            var s = new SmtpSettings();
            Assert.Equal("smtp.office365.com", s.Host);
            Assert.Equal(587, s.Port);
            Assert.True(s.UseStartTls);
        }
    }
}
