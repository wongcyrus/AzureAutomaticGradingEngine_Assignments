namespace GraderFunctionApp.Constants
{
    public static class HtmlTemplates
    {
        public const string GraderForm = @"
<!DOCTYPE html>
<html lang='en' xmlns='http://www.w3.org/1999/xhtml'>
<head>
    <meta charset='utf-8' />
    <title>Azure Grader</title>
</head>
<body>
    <form id='contact-form' method='post'>
        Azure Credentials<br/>
        <textarea name='credentials' required  rows='15' cols='100'></textarea>
        <br/>
        NUnit Test Name<br/>
        <input type='text' id='filter' name='filter' size='50'/><br/>
        <button type='submit'>Run Test</button>
    </form>
    <footer>
        <p>Developed by <a href='https://www.vtc.edu.hk/admission/en/programme/it114115-higher-diploma-in-cloud-and-data-centre-administration/'> Higher Diploma in Cloud and Data Centre Administration Team.</a></p>
    </footer>
</body>
</html>";
    }
}
