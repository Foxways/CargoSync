using OrganizationImportTool.Auth;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class UserStoreTests : IDisposable
    {
        private readonly TempFile _db = new(".db");
        private readonly UserStore _store;

        public UserStoreTests()
        {
            _store = new UserStore(_db.Path);
            _store.EnsureSchema();
            Assert.Null(_store.CreateUser("alice", "secret123", "pet"));
            Assert.Null(_store.CreateUser("bob", "hunter22", null));
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void First_created_user_is_admin()
        {
            Assert.True(_store.Authenticate("alice", "secret123")!.IsAdmin);
            Assert.False(_store.Authenticate("bob", "hunter22")!.IsAdmin);
        }

        [Fact]
        public void Five_wrong_passwords_lock_the_account()
        {
            for (int i = 0; i < 4; i++)
            {
                var r = _store.AuthenticateDetailed("bob", "wrong");
                Assert.Null(r.User);
                Assert.False(r.Locked);
            }
            var fifth = _store.AuthenticateDetailed("bob", "wrong");
            Assert.True(fifth.Locked);

            // Even the CORRECT password is rejected while locked (persisted lock).
            var locked = _store.AuthenticateDetailed("bob", "hunter22");
            Assert.True(locked.Locked);
            Assert.Null(locked.User);
        }

        [Fact]
        public void Successful_login_resets_the_failure_counter()
        {
            _store.AuthenticateDetailed("bob", "wrong");
            _store.AuthenticateDetailed("bob", "wrong");
            Assert.NotNull(_store.Authenticate("bob", "hunter22"));
            for (int i = 0; i < 4; i++) _store.AuthenticateDetailed("bob", "wrong");
            // only 4 fails since the reset - not locked yet
            Assert.NotNull(_store.Authenticate("bob", "hunter22"));
        }

        [Fact]
        public void ChangePassword_requires_the_current_password()
        {
            Assert.NotNull(_store.ChangePassword("bob", "WRONG", "newpass1"));   // rejected
            Assert.Null(_store.ChangePassword("bob", "hunter22", "newpass1"));   // ok
            Assert.Null(_store.Authenticate("bob", "hunter22"));
            Assert.NotNull(_store.Authenticate("bob", "newpass1"));
        }

        [Fact]
        public void AdminResetPassword_requires_a_real_admin()
        {
            // bob is not an admin
            Assert.NotNull(_store.AdminResetPassword("bob", "hunter22", "alice", "newpass1"));
            // wrong admin password
            Assert.NotNull(_store.AdminResetPassword("alice", "WRONG", "bob", "newpass1"));
            // real admin works
            Assert.Null(_store.AdminResetPassword("alice", "secret123", "bob", "newpass1"));
            Assert.NotNull(_store.Authenticate("bob", "newpass1"));
        }

        [Fact]
        public void Unknown_user_fails_like_wrong_password()
        {
            var r = _store.AuthenticateDetailed("nobody", "x");
            Assert.Null(r.User);
            Assert.False(r.Locked);
        }
    }
}
