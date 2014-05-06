using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Mail;
using log4net.Appender;
using log4net.Core;
using log4net.Layout.Pattern;
using Topshelf.Options;

namespace PsychNet.EventService.Logging
{
    public class AppSettingLookupPatternConverter : PatternLayoutConverter
    {
        protected override void Convert(System.IO.TextWriter writer, LoggingEvent loggingEvent)
        {
            writer.Write(ConfigurationManager.AppSettings[Option]);
        }
    }

    public class SmtpClientSmtpAppender : SmtpAppender
    {
        override protected void SendBuffer(LoggingEvent[] events)
        {
            try
            {
                StringWriter writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                string t = Layout.Header;
                if (t != null)
                {
                    writer.Write(t);
                }
                for (int i = 0; i < events.Length; i++)
                {
                    // Render the event and append the text to the buffer
                    RenderLoggingEvent(writer, events[i]);
                }
                t = Layout.Footer;
                if (t != null)
                {
                    writer.Write(t);
                }
                // Use SmtpClient so we can use SSL.
                SmtpClient client = new SmtpClient(SmtpHost, Port);
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(Username, Password);
                string messageText = writer.ToString();
                MailMessage mail = new MailMessage(From, To, Subject, messageText);
                client.Send(mail);
            }
            catch (Exception e)
            {
                ErrorHandler.Error("Error occurred while sending e-mail notification from SmtpClientSmtpAppender.", e);
            }
        }
    }
}