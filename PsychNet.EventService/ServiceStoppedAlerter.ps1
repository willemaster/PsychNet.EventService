<# Power shell Script to send email
    by Thom McKiernan - thommck.wordpress.com - @thommck
    Created 28/03/2011
    #>
 
# Get the state of any services starting IBM* from computer Server1
$service = Get-WmiObject win32_service -computername YOGA | select name,state | where { $_.name -like "PsychNet*"} | out-string
# Specify a sender email address
$emailFrom = "ops@sessionbase.com"
# Specify a recipient email address
$emailTo = "will.master@gmail.com"
# Put in a subject line
$subject = "Sessionbase Service Alert"
# Add the Service state from line 6 to some body text
$body = $service 
# Put the DNS name or IP address of your SMTP Server
$smtpServer = "smtp.gmail.com"
$smtp = new-object Net.Mail.SmtpClient($smtpServer, 587)
$smtp.EnableSsl = $true
$smtp.Credentials = New-Object System.Net.NetworkCredential("purelyfabricated@gmail.com", "qwepoiqwepoi");
# This line pieces together all the info into an email and sends it
$smtp.Send($emailFrom, $emailTo, $subject, $body)