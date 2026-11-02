using AlaraERP.Application.Notifications;
using AlaraERP.Domain.Notifications;

namespace AlaraERP.UnitTests.Domain;

public sealed class WhatsAppTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static WhatsAppMessage Pending() => new(Guid.NewGuid(), "96170000001", "Alert", WhatsAppStatus.Pending, Now);

    [Fact]
    public void Template_text_has_no_line_breaks_tabs_or_long_runs_of_spaces()
    {
        Assert.Equal("Mask is low - Alert level: 5. Top it up.", WhatsAppText.ForTemplate("Mask is low\nAlert level:\t5.     Top it up."));
        Assert.Equal("One - Two", WhatsAppText.ForTemplate("One\r\n\r\n  Two\n"));
    }

    [Fact]
    public void A_long_alert_is_shortened_but_keeps_its_link()
    {
        const string link = "https://erp.test/orders/1";
        var text = WhatsAppText.ForTemplate(new string('x', 2000) + " " + link);

        Assert.True(text.Length <= WhatsAppText.MaxTemplateText);
        Assert.EndsWith("… " + link, text);
        Assert.True(WhatsAppText.ForTemplate(new string('x', 2000)).Length <= WhatsAppText.MaxTemplateText);
    }

    [Fact]
    public void Failed_attempts_wait_longer_each_time_then_give_up()
    {
        var message = Pending();
        var waits = new List<TimeSpan>();
        for (var i = 0; i < WhatsAppMessage.MaxAttempts - 1; i++)
        {
            message.MarkAttemptFailed("503", permanent: false, Now);
            Assert.Equal(WhatsAppStatus.Pending, message.Status);
            waits.Add(message.NextAttemptAt!.Value - Now);
        }
        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)], waits);

        message.MarkAttemptFailed("503", permanent: false, Now);
        Assert.Equal(WhatsAppStatus.Failed, message.Status);
        Assert.Null(message.NextAttemptAt);
    }

    [Fact]
    public void A_report_about_an_old_attempt_does_not_touch_a_message_waiting_to_be_sent()
    {
        var message = Pending();
        message.ApplyDeliveryReport(WhatsAppStatus.Delivered, null);
        message.ApplyDeliveryReport(WhatsAppStatus.Failed, "nope");

        Assert.Equal(WhatsAppStatus.Pending, message.Status);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void Recipients_need_a_country_code_and_are_counted_once()
    {
        var options = new WhatsAppOptions { Recipients = ["+961 70 123 456", "96170123456", "03 123 456", "123"] };

        Assert.Equal(["96170123456"], options.RecipientPhones);
    }

    [Fact]
    public void Setup_lists_what_is_missing_until_it_can_send()
    {
        Assert.Equal(["WhatsApp__Enabled", "WhatsApp__PhoneNumberId", "WhatsApp__AccessToken", "WhatsApp__Recipients__0"], new WhatsAppOptions().MissingSettings);
        Assert.False(new WhatsAppOptions { Enabled = true, PhoneNumberId = "1", AccessToken = "t", Recipients = ["03 123 456"] }.IsConfigured);
        Assert.True(new WhatsAppOptions { Enabled = true, PhoneNumberId = "1", AccessToken = "t", Recipients = ["96170123456"] }.IsConfigured);
    }
}
