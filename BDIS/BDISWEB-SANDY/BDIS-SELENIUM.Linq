<Query Kind="Program">
  <Namespace>System.Net.Http</Namespace>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

// 1. AUXILIARY CLASS FOR STEPS AND REPORTS (Same structured design)
public class TestStep
{
    public string Name { get; set; }
    public string Status { get; set; } // PASSED or FAILED
    public string Details { get; set; }
    public string ScreenshotPath { get; set; }
    public string ExecutedBy { get; set; }
}

public class TestWebResult
{
    public string WebKey { get; set; }
    public string Url { get; set; }
    public string Status { get; set; } // PASSED or FAILED
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public string ErrorMessage { get; set; } = "";
}

void Main()
{
    // ==========================================
    // DYNAMIC TEST NAME CONFIGURATION
    // ==========================================
    string testName = "BDIS"; // Change to "ICA", "IDIS", or "WFMS" depending on the script
    string siteLabel = "File Server (F5)"; // Friendly name of the only environment under test
    
    string currentUser = Environment.UserName;
    string webhookUrl = "https://default1aed4588b8ce43a8a775989538fd30.d8.environment.api.powerplatform.com:443/powerautomate/automations/direct/workflows/218579758dd34aec91459a1858259537/triggers/manual/paths/invoke?api-version=1&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=9B_4JUjNCp4wOLA8gBC-1kFsPGq7m6CqV6wuoGHRQYE";

    var configPath = $@"C:\Selenium\{testName}WEB-SANDY\config.properties";
    if (!File.Exists(configPath)) throw new FileNotFoundException($"Configuration file missing at: {configPath}");
    
    var config = LoadConfig(configPath);
    var urls = new[] { "url1" }; // File Server (F5)
    
    string logFolder = $@"C:\Selenium\{testName}WEB-SANDY\log\";
    if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var htmlReportPath = Path.Combine(logFolder, $"{testName}WebApplication_{timestamp}.html");
    var logPath = Path.Combine(logFolder, $"{testName}WebApplication.txt");
    
    List<TestWebResult> webResults = new List<TestWebResult>();
    List<TestStep> allTestSteps = new List<TestStep>();
    bool globalFailure = false;

    $"=== STARTING {testName} WEB AUTOMATION TEST ===".Dump();

    foreach (var urlKey in urls)
    {
        if (!config.ContainsKey(urlKey)) continue; // Skip missing entries defensively
        var actualUrl = config[urlKey];
        var currentResult = new TestWebResult
        {
            WebKey = siteLabel,
            Url = actualUrl,
            StartTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Status = "PASSED"
        };

        $"Starting evaluation for {siteLabel} ({urlKey}): {actualUrl}".Dump();
        EdgeDriver driver = SetupEdgeDriver();
        
        // Intermediate inner function to map flow steps to the new visual report
        void RunStep(string stepName, Action action)
        {
            try
            {
                action();
                allTestSteps.Add(new TestStep { Name = $"[{siteLabel}] {stepName}", Status = "PASSED", Details = "Executed successfully.", ExecutedBy = currentUser });
                LogStep(stepName, "Passed", logPath);
            }
            catch (Exception ex)
            {
                globalFailure = true;
                currentResult.Status = "FAILED";
                currentResult.ErrorMessage = ex.Message;

                string screenshotName = $"Screenshot_Error_{urlKey}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string fullScreenshotPath = Path.Combine(logFolder, screenshotName);
                string savedPath = TakeScreenshot(driver, fullScreenshotPath);

                allTestSteps.Add(new TestStep { 
                    Name = $"[{siteLabel}] {stepName}", 
                    Status = "FAILED", 
                    Details = ex.Message, 
                    ScreenshotPath = !string.IsNullOrEmpty(savedPath) ? savedPath : "Could not capture screenshot.",
                    ExecutedBy = currentUser 
                });

                LogStep(stepName, $"Failed: {ex.Message}", logPath);
                throw; // Breaks the flow of this current URL
            }
        }
        
        try
        {
            config["url"] = config[urlKey];
            config["usrName"] = config[$"usrName{urlKey.Last()}"];
            config["usrPass"] = config[$"usrPass{urlKey.Last()}"];

            // Step flow controlled by the new visual interceptor
            RunStep("Login", () => Login(driver, config));
            RunStep("Document Query", () => DocumentQuery(driver, config));
            RunStep("Web Status", () => WebStatus(driver, config));
            RunStep("Packet Print", () => PacketPrint(driver, config));
            RunStep("Doc Type", () => DocType(driver, config));
            RunStep("Upload Document", () => UploadDocument(driver, config));
            RunStep("Account Management", () => AccountManagement(driver, config));
            RunStep("Manipulate Image", () => ManipulateImage(driver, config));
        }
        catch (Exception)
        {
            // Exception already handled and integrated in RunStep
        }
        finally
        {
            driver.Quit();
            currentResult.EndTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            webResults.Add(currentResult);
            
            $"[{currentResult.Status}] Finished {urlKey}".Dump();
        }
    }

    // 3. GENERATE THE HTML REPORT WITH THE NEW ELEGANT DESIGN
    GenerateHtmlReport(htmlReportPath, allTestSteps, testName);
    
    // 4. SEND NOTIFICATION SHOWING THE JSON IN CONSOLE
    SendWebhookNotification(webhookUrl, testName, currentUser, globalFailure, webResults);

    $"\n=== PROCESS COMPLETED ===".Dump();
    $"HTML Report successfully generated at: {htmlReportPath}".Dump();
}

private string TakeScreenshot(IWebDriver driver, string savePath)
{
    try
    {
        if (driver != null)
        {
            Screenshot screenshot = ((ITakesScreenshot)driver).GetScreenshot();
            screenshot.SaveAsFile(savePath); 
            return savePath;
        }
    }
    catch {}
    return null;
}

private void SendWebhookNotification(string url, string testName, string user, bool hasFailed, List<TestWebResult> results)
{
    if (string.IsNullOrEmpty(url)) return;

    try
    {
        using (HttpClient client = new HttpClient())
        {
            string globalStatus = hasFailed ? "FAILED" : "PASSED";
            
            StringBuilder jsonResultsBuilder = new StringBuilder();
            jsonResultsBuilder.Append("[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                jsonResultsBuilder.Append("{");
                jsonResultsBuilder.Append($"\"webKey\": \"{EscapeJsonString(r.WebKey)}\",");
                jsonResultsBuilder.Append($"\"targetUrl\": \"{EscapeJsonString(r.Url)}\",");
                jsonResultsBuilder.Append($"\"status\": \"{EscapeJsonString(r.Status)}\",");
                jsonResultsBuilder.Append($"\"startTime\": \"{EscapeJsonString(r.StartTime)}\",");
                jsonResultsBuilder.Append($"\"endTime\": \"{EscapeJsonString(r.EndTime)}\",");
                jsonResultsBuilder.Append($"\"error\": \"{EscapeJsonString(r.ErrorMessage)}\"");
                jsonResultsBuilder.Append("}");
                if (i < results.Count - 1) jsonResultsBuilder.Append(",");
            }
            jsonResultsBuilder.Append("]");

            // Clean construction ensuring JSON format

			string jsonPayload = $"{{" +
			    $"\"testName\": \"{EscapeJsonString(testName)}\"," +
			    $"\"user\": \"{EscapeJsonString(user)}\"," +
			    $"\"globalStatus\": \"{EscapeJsonString(globalStatus)}\"," +
			    $"\"results\": {jsonResultsBuilder.ToString()}" + 
			$"}}";
            
            // Shows the exact JSON in console for you to validate
            $"\n[Payload JSON sent to Webhook]:\n{jsonPayload}\n".Dump();
            
            HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = client.PostAsync(url, content).Result;
            
            if (response.IsSuccessStatusCode)
            {
                "✔ Power Automate cloud workflow triggered successfully.".Dump();
            }
            else
            {
                $"❌ Webhook failed with status code: {response.StatusCode}".Dump();
                string errorBody = response.Content.ReadAsStringAsync().Result;
                $"[Server Detail]: {errorBody}".Dump();
            }
        }
    }
    catch (Exception ex) { $"❌ Webhook error: {ex.Message}".Dump(); }
}

// CRUCIAL HELPER FUNCTION: Safely escapes strings for manual JSON
private string EscapeJsonString(string value)
{
    if (string.IsNullOrEmpty(value)) return "";
    
    StringBuilder sb = new StringBuilder();
    foreach (char c in value)
    {
        switch (c)
        {
            case '\"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\b': sb.Append("\\b"); break;
            case '\f': sb.Append("\\f"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            default:
                if (int.Parse(string.Format("{0:X4}", (int)c), System.Globalization.NumberStyles.HexNumber) < 32)
                {
                    // Avoid broken ASCII control characters
                    sb.Append(string.Format("\\u{0:X4}", (int)c));
                }
                else
                {
                    sb.Append(c);
                }
                break;
        }
    }
    return sb.ToString();
}

private void GenerateHtmlReport(string filePath, List<TestStep> steps, string testName)
{
    int totalSteps = steps.Count;
    int failedSteps = steps.Count(s => s.Status == "FAILED");
    int passedSteps = totalSteps - failedSteps;
    string globalStatus = failedSteps > 0 ? "FAILED" : "PASSED";
    string globalStatusColor = failedSteps > 0 ? "#F56565" : "#48BB78";
    string operatorUser = steps.Count > 0 ? steps[0].ExecutedBy : "Unknown";

    StringBuilder sb = new StringBuilder();
    sb.Append("<html><head><meta charset='utf-8'><title>" + testName + " Automation Dashboard</title>");
    sb.Append("<style>");
    sb.Append("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background-color: #1A202C; color: #E2E8F0; }");
    sb.Append(".header-container { display: flex; justify-content: space-between; align-items: center; border-bottom: 3px solid #4A5568; padding-bottom: 20px; margin-bottom: 30px; }");
    sb.Append("h2 { color: #ECC94B; margin: 0; font-size: 28px; font-weight: 600; }");
    sb.Append(".timestamp { color: #A0AEC0; font-size: 14px; }");
    sb.Append(".summary-deck { display: flex; gap: 20px; margin-bottom: 30px; }");
    sb.Append(".card { flex: 1; background: #2D3748; border-radius: 8px; padding: 15px 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.2); border-left: 5px solid #4A5568; }");
    sb.Append(".card.status-card { border-left-color: " + globalStatusColor + "; }");
    sb.Append(".card.agent-card { border-left-color: #63B3ED; }");
    sb.Append(".card-title { font-size: 12px; text-transform: uppercase; color: #A0AEC0; font-weight: bold; letter-spacing: 1px; }");
    sb.Append(".card-value { font-size: 20px; font-weight: bold; margin-top: 5px; color: #FFF; }");
    sb.Append("table { width: 100%; border-collapse: collapse; background: #2D3748; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }");
    sb.Append("th { background-color: #1A202C; color: #ECC94B; font-weight: 600; text-transform: uppercase; font-size: 13px; letter-spacing: 0.5px; padding: 15px; text-align: left; }");
    sb.Append("td { padding: 15px; border-bottom: 1px solid #4A5568; font-size: 14px; vertical-align: top; }");
    sb.Append("tr:last-child td { border-bottom: none; }");
    sb.Append("tr:hover { background-color: #3A475C; }");
    sb.Append(".badge { padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 11px; display: inline-block; text-transform: uppercase; }");
    sb.Append(".badge-success { background-color: #2F855A; color: #C6F6D5; }");
    sb.Append(".badge-danger { background-color: #9B2C2C; color: #FED7D7; }");
    sb.Append("pre { background: #1A202C; padding: 12px; border-radius: 6px; border-left: 4px solid #F56565; white-space: pre-wrap; color: #FEB2B2; font-size: 12px; font-family: 'Courier New', Courier, monospace; margin-top: 8px; }");
    sb.Append(".screenshot-link { display: inline-block; margin-top: 8px; color: #63B3ED; text-decoration: none; font-weight: bold; font-size: 13px; }");
    sb.Append(".screenshot-link:hover { text-decoration: underline; color: #4299E1; }");
    sb.Append("</style></head><body>");

    sb.Append("<div class='header-container'>");
    sb.Append("<div><h2>Automated Test Execution Dashboard</h2><div style='color:#A0AEC0; margin-top:5px;'>" + testName + " Web Application Evaluation</div></div>");
    sb.Append("<div class='timestamp'><strong>Execution Date:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
    sb.Append("</div>");

    sb.Append("<div class='summary-deck'>");
    sb.Append("<div class='card agent-card'><div class='card-title'>PCM Operator Agent</div><div class='card-value'>" + operatorUser + "</div></div>");
    sb.Append("<div class='card status-card'><div class='card-title'>Global Evaluation Status</div><div class='card-value' style='color:" + globalStatusColor + ";'>" + globalStatus + "</div></div>");
    sb.Append("<div class='card'><div class='card-title'>Consolidated Steps</div><div class='card-value'>" + passedSteps + " / " + totalSteps + " Steps Passed</div></div>");
    sb.Append("</div>");

    sb.Append("<table>");
    sb.Append("<tr><th style='width:15%;'>#</th><th style='width:35%;'>Test Step Context</th><th style='width:50%;'>Execution Details / Logs</th></tr>");

    for (int i = 0; i < steps.Count; i++)
    {
        var step = steps[i];
        bool isFailed = step.Status == "FAILED";
        string badgeStyle = isFailed ? "badge-danger" : "badge-success";

        sb.Append("<tr>");
        sb.Append("<td>" + (i + 1) + "</td>");
        sb.Append("<td><strong>" + System.Net.WebUtility.HtmlEncode(step.Name) + "</strong></td>");

        if (isFailed)
        {
            sb.Append("<td>");
            sb.Append("<span class='badge " + badgeStyle + "'>ERROR</span>");
            sb.Append("<pre>" + System.Net.WebUtility.HtmlEncode(step.Details) + "</pre>");
            if (!string.IsNullOrEmpty(step.ScreenshotPath))
            {
                sb.Append("<br><a class='screenshot-link' href='" + step.ScreenshotPath + "' target='_blank'>👁 View Error Screenshot</a>");
            }
            sb.Append("</td>");
        }
        else
        {
            sb.Append("<td><span class='badge " + badgeStyle + "' style='margin-right:10px;'>PASSED</span><span style='color: #CBD5E0;'>" + System.Net.WebUtility.HtmlEncode(step.Details) + "</span></td>");
        }
        sb.Append("</tr>");
    }

    sb.Append("</table></body></html>");
    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
}

#region Support and Infrastructure Methods
EdgeDriver SetupEdgeDriver()
{
    var options = new EdgeOptions();
    options.AddArgument("--ignore-certificate-errors");
    options.AddArgument("--allow-insecure-localhost");
    var service = EdgeDriverService.CreateDefaultService(@"C:\Selenium\");
    service.HideCommandPromptWindow = true;
    return new EdgeDriver(service, options);
}

Dictionary<string, string> LoadConfig(string path)
{
    var dict = new Dictionary<string, string>();
    foreach (var line in File.ReadAllLines(path))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2) dict[parts[0].Trim()] = parts[1].Trim();
        }
    }
    return dict;
}

void LogStep(string step, string result, string logPath)
{
    var logLine = $"{DateTime.Now:MM/dd/yyyy HH:mm:ss}\t{step}\t{result}";
    File.AppendAllText(logPath, logLine + Environment.NewLine);
}

void Login(EdgeDriver driver, Dictionary<string, string> config) { driver.Navigate().GoToUrl(config["url"]); Thread.Sleep(1000); driver.FindElement(By.XPath(config["usrNameTxtBox"])).SendKeys(config["usrName"]); driver.FindElement(By.XPath(config["usrPassTxtBox"])).SendKeys(config["usrPass"]); new SelectElement(driver.FindElement(By.XPath(config["countryLogin"]))).SelectByValue(config["countryCode"]); driver.FindElement(By.XPath(config["loginBtn"])).Click(); }
void DocumentQuery(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[2]/td/a")).Click(); Thread.Sleep(1000); var shipmentNbr = config["countryCode"] == "CA" ? config["caShipmentNbr"] : config["usShipmentNbr"]; driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table[1]/tbody/tr[6]/td[3]/input")).SendKeys(shipmentNbr); driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table[1]/tbody/tr[3]/td[3]/input")).Click(); Thread.Sleep(1000); }
void WebStatus(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[6]/td/a")).Click(); Thread.Sleep(1000); }
void PacketPrint(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[3]/td/a")).Click(); Thread.Sleep(1000); }
void DocType(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[7]/td/a")).Click(); Thread.Sleep(1000); driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[3]/td/div/table/tbody/tr[3]/td/table/tbody/tr[7]/td[2]/a")).Click(); Thread.Sleep(1000); driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[3]/td/div/table/tbody/tr[3]/td/table/tbody/tr[1]/td/p[2]/input")).Click(); Thread.Sleep(1000); }
void UploadDocument(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[9]/td/a")).Click(); Thread.Sleep(1000); new SelectElement(driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[3]/td[2]/select"))).SelectByValue("1"); driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[4]/td/input")).Click(); Thread.Sleep(1000); }
void AccountManagement(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[5]/td/a")).Click(); Thread.Sleep(1000); new SelectElement(driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[2]/td/div/select[1]"))).SelectByValue(config["usrName"]); driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[2]/td/div/input[6]")).SendKeys(config["usrPass"]); driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/table/tbody/tr[3]/td[1]/input[4]")).Click(); Thread.Sleep(1000); }
void ManipulateImage(EdgeDriver driver, Dictionary<string, string> config) { driver.FindElement(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[1]/table/tbody/tr[10]/td/a")).Click(); Thread.Sleep(2000); var imagePath = @"C:\Selenium\BDISWEB-SANDY\BDISImg\Image.tif"; var pendingWorks = driver.FindElements(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/div/div/table/tbody/tr[5]/td[2]/input")); var uploadInputs = driver.FindElements(By.XPath("/html/body/form/div[3]/table/tbody/tr/td[2]/section/div/div/table/tbody/tr[4]/td[2]/div/input")); if (uploadInputs.Count > 0) { uploadInputs[0].Click(); } else if (pendingWorks.Count > 0) { pendingWorks[0].Click(); } else { throw new Exception("Upload elements or pending work inputs were missing."); } Thread.Sleep(2000); }
#endregion