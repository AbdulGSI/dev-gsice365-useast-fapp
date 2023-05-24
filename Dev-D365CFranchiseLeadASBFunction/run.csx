#r "Newtonsoft.Json"
#r "Microsoft.IdentityModel.Clients.ActiveDirectory"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

public static void Run(string myQueueItem, ILogger log)
{
    Guid leadId = Guid.Empty;
    Guid noteId = Guid.Empty;
    log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
    //deserialize input message to Account object     
    Lead lead = JsonConvert.DeserializeObject<Lead>(myQueueItem);
    //Fething Client Data from Environment
    var clientId = Environment.GetEnvironmentVariable("gsi-dev_ClientId");
    log.LogInformation($"clientid: {clientId}");
    var clientSecret = Environment.GetEnvironmentVariable("gsi-dev_ClientSecret");
    log.LogInformation($"clientSecret: {clientSecret}");
    var clientResource = Environment.GetEnvironmentVariable("gsi-dev_Resource");
    log.LogInformation($"clientResource: {clientResource}");
    var clientWebApiUrl = Environment.GetEnvironmentVariable("gsi-dev_WebApiUrl");
    log.LogInformation($"clientWebApiUrl: {clientWebApiUrl}");
    //Set Dynamics Web API access details 
    AccessDetails accessDetails = new AccessDetails
    {
        ApplicationId = clientId,
        Secret = clientSecret,
        WebAPIURL = clientWebApiUrl,
        Resource = clientResource
    };
    //Get Access Token 
    string token = string.Empty;
    token = GetWebAPIAccessToken(accessDetails, log);
    if (token != string.Empty)
    {

        using (var client = new HttpClient())
        {
            client.BaseAddress = new Uri(accessDetails.WebAPIURL);
            client.Timeout = new TimeSpan(0, 2, 0);
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Add("MSCRM.SuppressDuplicateDetection", "false");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            try
            {
                var leadIdstring = FetchCEEntityData(client, accessDetails, RemoveEscapeChars(lead.ClientId), "leads", log).Result;
                if (leadIdstring != string.Empty)
                {
                    leadId = new Guid(leadIdstring);
                    log.LogInformation($"LeadId Retrieved: {leadIdstring}");
                }
            }
            catch (Exception exception)
            {
                log.LogInformation($"Exception: {exception.Message}");
                throw new Exception($"Something is wrong: {exception.Message}");
            }
        }

        if (leadId == null || leadId == Guid.Empty)
        {
            //Create lead record in Dynamics 365 
            leadId = CreateLeadInCE(lead, accessDetails, token, log).Result;

            log.LogInformation($"Newly Created LeadId: {leadId.ToString()}");
            if (leadId != Guid.Empty)
            {
                //Create note record in Dynamics 365 
                noteId = CreateNotesInCE(lead, accessDetails, token, log, leadId.ToString()).Result;
                log.LogInformation($"Newly Created NoteId: {noteId.ToString()}");
            }
            else
            {
                log.LogInformation($"Unable to Create Note record As LeadId is null.");
            }

        }
        else
        {
            //Create lead record in Dynamics 365 
            leadId = UpdateLeadInCE(leadId, lead, accessDetails, token, log).Result;
        }

    }
    else
    {
        log.LogInformation($"Unable to retrieve AccesToken");
    }
}

// Get Web API access token 
private static string GetWebAPIAccessToken(AccessDetails accessDetails, ILogger log)
{
    string result = string.Empty;
    string accessToken = string.Empty;
    string webAPIURL = accessDetails.WebAPIURL;
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    string resource = accessDetails.Resource;
    try
    {
        ClientCredential clientCrendential = new ClientCredential(accessDetails.ApplicationId, accessDetails.Secret);
        AuthenticationParameters ap = AuthenticationParameters.CreateFromResourceUrlAsync(new Uri(webAPIURL)).Result;
        AuthenticationContext authContext = new AuthenticationContext(ap.Authority);
        accessToken = authContext.AcquireTokenAsync(resource, clientCrendential).Result.AccessToken;
        log.LogInformation($"AccessToken Retrieved: {accessToken}");
    }
    catch (Exception exception)
    {
        log.LogInformation($"Exception: {exception.Message}");
        throw new Exception($"AccessToken couldn't Retrieved: {exception.Message}");
    }
    return accessToken;
}
// Create Dynamics 365 Lead record using access details and access token 
private static async Task<Guid> CreateLeadInCE(Lead lead, AccessDetails accessDetails, string accessToken, ILogger log)
{
    Guid result = Guid.Empty;
    string leadSource = string.Empty;
    string leadSourceId = string.Empty;
    string leadOriginId = string.Empty;
    string zipcode = string.Empty;
    string zipcodeId = string.Empty;
    int? emailValidationStatus = lead.EmailValidationStatus;
    int? phoneValidationStatus = lead.PhoneValidationStatus;

    log.LogInformation($"Phone Response" + lead.EmailValidationResponse);
    log.LogInformation($"Email Response" + lead.PhoneValidationResponse);

    ///EmailValidationResponse emailValidationResponse = JsonConvert.DeserializeObject<EmailValidationResponse>(lead.EmailValidationResponse);
    ///PhoneValidationResponse phoneValidationResponse = JsonConvert.DeserializeObject<PhoneValidationResponse>(lead.PhoneValidationResponse);

    string phoneType = RemoveEscapeChars(lead.PhoneType);

    //Convert lead to JObject     
    JObject leadObj = new JObject { };
    leadObj["gsi_clientid"] = RemoveEscapeChars(lead.ClientId);
    leadObj["firstname"] = RemoveEscapeChars(lead.FirstName);
    leadObj["lastname"] = RemoveEscapeChars(lead.LastName);
    leadObj["emailaddress1"] = RemoveEscapeChars(lead.Email);

    if (emailValidationStatus != null)
    {
        leadObj["gsi_emailvalidationstatus"] = emailValidationStatus;
    }

    if (phoneType != null)
    {
        if (phoneType.ToLower() == "home")
        {
            // telephone2 - Home Phone
            leadObj["telephone2"] = RemoveEscapeChars(lead.Phone);
            if (phoneValidationStatus != null)
            {
                leadObj["gsi_homephonevalidationstatus"] = phoneValidationStatus;
            }
        }
        else if (phoneType.ToLower() == "mobile")
        {
            // mobilephone - Mobile Phone
            leadObj["mobilephone"] = RemoveEscapeChars(lead.Phone);
            if (phoneValidationStatus != null)
            {
                leadObj["gsi_mobilephonevalidationstatus"] = phoneValidationStatus;
            }
        }
        else if (phoneType.ToLower() == "business" || phoneType.ToLower() == "landline")
        {
            // telephone1 - BusinessPhone
            leadObj["telephone1"] = RemoveEscapeChars(lead.Phone);
            if (phoneValidationStatus != null)
            {
                leadObj["gsi_businessphonevalidationstatus"] = phoneValidationStatus;
            }
        }
        else
        {
            // telephone1 - BusinessPhone
            leadObj["telephone2"] = RemoveEscapeChars(lead.Phone);
            leadObj["gsi_homephonevalidationstatus"] = 2;

        }
    }
    else
    {
        // telephone1 - BusinessPhone
        leadObj["telephone2"] = RemoveEscapeChars(lead.Phone);
        leadObj["gsi_homephonevalidationstatus"] = 2;
    }

    leadObj["subject"] = "Franchise Lead " + "- " + RemoveEscapeChars(lead.LastName) + ", " + RemoveEscapeChars(lead.FirstName);
    leadObj["gsi_usersignupforemails"] = lead.UserEmailOptedIn;
    leadObj["gsi_usersignupforcalls"] = lead.UserPhoneOptedIn;
    leadObj["gsi_usersignupforsms"] = lead.UserSMSOptedIn;
    leadObj["gsi_ipaddress"] = RemoveEscapeChars(lead.IPAddress);
    leadObj["gsi_gacampaign"] = RemoveEscapeChars(lead.GaCampaign);
    leadObj["gsi_gaid"] = RemoveEscapeChars(lead.GaClientID);
    leadObj["gsi_galandingpage"] = RemoveEscapeChars(lead.GaLandingPage);
    leadObj["gsi_gamedium"] = RemoveEscapeChars(lead.GaMedium);
    leadObj["gsi_gasource"] = RemoveEscapeChars(lead.GaSource);
    leadObj["gsi_liquidity"] = lead.Liquidity;
    leadObj["gsi_emailvalidationresponse"] = lead.EmailValidationResponse;
    leadObj["gsi_phonevalidationresponse"] = lead.PhoneValidationResponse;


    if (lead.LeadMarketsofInterest != null && lead.LeadMarketsofInterest.ToArray().Length > 0)
    {
        leadObj["gsi_leadmarketsofinterest"] = string.Join(",", lead.LeadMarketsofInterest.ToArray());
    }
    //log.LogInformation($"LeadMarketsofInterest : {string.Join( ",", lead.LeadMarketsofInterest.ToArray() )}");
    leadObj["gsi_othermarketsofinterest"] = RemoveEscapeChars(lead.OtherMarketsofInterest);

    leadObj.Add("gsi_licensetype", 100000001);
    leadObj.Add("gsi_leadcreatedfrom", 100000000);


    if (lead.Liquidity > 2 && lead.LeadMarketsofInterest.ToArray().Length > 1)
    {
        leadObj.Add("leadqualitycode", 1);
    }
    else if (lead.Liquidity > 2 && lead.LeadMarketsofInterest.ToArray().Length == 1 && !lead.LeadMarketsofInterest.ToArray().Contains(99))
    {
        leadObj.Add("leadqualitycode", 1);
    }
    else if (lead.Liquidity > 2 && lead.LeadMarketsofInterest.ToArray().Length == 1 && lead.LeadMarketsofInterest.ToArray().Contains(99))
    {
        leadObj.Add("leadqualitycode", 2);
    }
    else if (lead.Liquidity < 3 && lead.LeadMarketsofInterest.ToArray().Length > 1)
    {
        leadObj.Add("leadqualitycode", 2);
    }
    else if (lead.Liquidity < 3 & lead.LeadMarketsofInterest.ToArray().Length == 1 && !lead.LeadMarketsofInterest.ToArray().Contains(99))
    {
        leadObj.Add("leadqualitycode", 2);
    }
    else
    {
        leadObj.Add("leadqualitycode", 3);
    }

    //Set channel & headers 
    using (var client = new HttpClient())
    {
        client.BaseAddress = new Uri(accessDetails.WebAPIURL);
        client.Timeout = new TimeSpan(0, 2, 0);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.DefaultRequestHeaders.Add("MSCRM.SuppressDuplicateDetection", "false");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            //Get LeadSource record Id
            leadSource = "37";
            if (leadSource != string.Empty)
            {
                leadSourceId = FetchCEEntityData(client, accessDetails, leadSource, "gsi_leadsources", log).Result;
                log.LogInformation($"LeadSource Id Retrieved: {leadSourceId}");
            }
            //Set Leadsource Id to Lead Object
            if (leadSourceId != string.Empty)
            {
                leadObj.Add("gsi_leadsource@odata.bind", "/gsi_leadsources(" + leadSourceId + ")");
            }

            //Get ZipCode Record Id
            zipcode = lead.Zip;
            if (zipcode != string.Empty)
            {
                zipcodeId = FetchCEEntityData(client, accessDetails, zipcode, "gsi_zipcodes", log).Result;
                log.LogInformation($"Zipcode Id Retrieved: {zipcodeId}");
            }
            //Set Zipcode Id to lead Object
            if (zipcodeId != string.Empty)
            {
                leadObj.Add("gsi_zipcodeid@odata.bind", "/gsi_zipcodes(" + zipcodeId + ")");
            }
            log.LogInformation($" Request : " + leadObj.ToString());
            //set Create request and content 
            HttpRequestMessage createRequest = new HttpRequestMessage(HttpMethod.Post, "leads")
            {
                Content = new StringContent(leadObj.ToString(), Encoding.UTF8, "application/json")
            };

            HttpResponseMessage createResponse = await client.SendAsync(createRequest);

            //verify successful call 
            createResponse.EnsureSuccessStatusCode();
            //get result content 
            string leadURL = createResponse.Headers.GetValues("OData-EntityId").FirstOrDefault();
            //extract GUID from URL
            result = Guid.Parse(Regex.Match(leadURL, @"\(([^)]*)\)").Groups[1].Value);
        }
        catch (Exception exception)
        {
            log.LogInformation($"Exception: {exception.Message}");
            throw new Exception($"Something is wrong: {exception.Message}");
        }
        return result;
    }
}

private static async Task<Guid> UpdateLeadInCE(Guid leadid, Lead lead, AccessDetails accessDetails, string accessToken, ILogger log)
{
    Guid result = Guid.Empty;
    var count = 0;

    var gsi_street = RemoveEscapeChars(lead.StreetAddress1);
    var address1_line2 = RemoveEscapeChars(lead.StreetAddress2);
    var address1_city = RemoveEscapeChars(lead.City);
    var address1_stateorprovince = RemoveEscapeChars(lead.State);

    if (gsi_street == string.Empty && address1_line2 == string.Empty && address1_city == string.Empty && address1_stateorprovince == string.Empty)
    {
        log.LogInformation($"Unable to Update lead record As Lead Address is blank");
        return result;
    }

    //Convert lead to JObject     
    JObject leadObj = new JObject { };
    leadObj["leadid"] = leadid;
    leadObj["gsi_experianaddress"] = true;

    leadObj["gsi_street"] = gsi_street;
    leadObj["address1_line1"] = gsi_street;
    leadObj["address1_line2"] = address1_line2;
    leadObj["address1_city"] = address1_city;
    leadObj["address1_stateorprovince"] = address1_stateorprovince;
    //leadObj["address1_postalcode"] = RemoveEscapeChars(lead.Zip);

    //Set channel & headers 
    using (var client = new HttpClient())
    {
        client.BaseAddress = new Uri(accessDetails.WebAPIURL);
        client.Timeout = new TimeSpan(0, 2, 0);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        //client.DefaultRequestHeaders.Add("MSCRM.SuppressDuplicateDetection", "false");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            log.LogInformation($"Request:" + accessDetails.WebAPIURL + "leads(" + leadid.ToString() + ")?$select=leadid");
            //set Create request and content 
            HttpRequestMessage updateRequest = new HttpRequestMessage(new HttpMethod("PATCH"), accessDetails.WebAPIURL + "leads(" + leadid.ToString() + ")?$select=leadid")
            {
                Content = new StringContent(leadObj.ToString(), Encoding.UTF8, "application/json")
            };

            HttpResponseMessage updateResponse = client.SendAsync(updateRequest).Result;

            //verify successful call 
            if (updateResponse.IsSuccessStatusCode)
            {
                result = leadid;
            }
        }
        catch (Exception exception)
        {
            log.LogInformation($"Exception: {exception.Message}");
            throw new Exception($"Something is wrong: {exception.Message}");
        }
        return result;
    }
}


//Retrieve CE Entity Record Data
private static async Task<string> FetchCEEntityData(HttpClient client, AccessDetails accessDetails, string websiteValue, string entityName, ILogger log)
{
    string fetchedGuid = string.Empty;
    string webApiUrl = accessDetails.WebAPIURL;
    string selectNameField = string.Empty;
    string value = string.Empty;
    var count = 0;
    string selectEntityName = entityName;
    log.LogInformation($"EntiyName: {selectEntityName}");
    string query = webApiUrl + selectEntityName + "/?$";
    try
    {
        if (selectEntityName == "gsi_leadsources")
        {
            selectNameField = "gsi_leadsourceid";
            value = websiteValue;
            var retrieveResponse = client.GetAsync(query + "select=" + selectNameField + "&$filter=gsi_value eq " + value).Result;
            if (retrieveResponse.IsSuccessStatusCode)
            {
                var parseResponse = JObject.Parse(retrieveResponse.Content.ReadAsStringAsync().Result);
                dynamic deserializeResponse = JsonConvert.DeserializeObject(parseResponse.ToString());
                count = deserializeResponse.value.Count;
                if (count != 0)
                {
                    fetchedGuid = deserializeResponse.value[0].gsi_leadsourceid.Value;
                    log.LogInformation($"leadsource: {fetchedGuid}");
                }
            }
            else
            {
                log.LogInformation($"CRM Record Id Couldn't Retrieved For LeadSource:");
            }
        }
        else if (selectEntityName == "gsi_zipcodes")
        {
            selectNameField = "gsi_zipcodeid";
            value = websiteValue;
            var retrieveResponseWithIsPrimary = client.GetAsync(query + "select=" + selectNameField + "&$filter=gsi_name eq " + "'" + value + "' and " + "gsi_isprimary eq true").Result;
            if (retrieveResponseWithIsPrimary.IsSuccessStatusCode)
            {
                var parseResponseWithIsPrimary = JObject.Parse(retrieveResponseWithIsPrimary.Content.ReadAsStringAsync().Result);
                dynamic deserializeResponseWithIsPrimary = JsonConvert.DeserializeObject(parseResponseWithIsPrimary.ToString());
                count = deserializeResponseWithIsPrimary.value.Count;
                log.LogInformation($"Count for ZipCode record with isPrimary value: {count}");
                if (count != 0)
                {
                    fetchedGuid = deserializeResponseWithIsPrimary.value[0].gsi_zipcodeid.Value;
                    log.LogInformation($"ZipCode: {fetchedGuid}");
                }
                else
                {
                    var retrieveResponseWithoutIsPrimary = client.GetAsync(query + "select=" + selectNameField + "&$filter=gsi_name eq " + "'" + value + "'").Result;
                    if (retrieveResponseWithoutIsPrimary.IsSuccessStatusCode)
                    {
                        var parseResponseWithoutIsPrimary = JObject.Parse(retrieveResponseWithoutIsPrimary.Content.ReadAsStringAsync().Result);
                        dynamic deserializeResponseWithoutIsPrimary = JsonConvert.DeserializeObject(parseResponseWithoutIsPrimary.ToString());
                        count = deserializeResponseWithoutIsPrimary.value.Count;
                        log.LogInformation($"Count for ZipCode record without isPrimary value: {count}");
                        if (count != 0)
                        {
                            fetchedGuid = deserializeResponseWithoutIsPrimary.value[0].gsi_zipcodeid.Value;
                            log.LogInformation($"ZipCode: {fetchedGuid}");
                        }
                        else
                        {
                            var retrieveResponseUnknown = client.GetAsync(query + "select=" + selectNameField + "&$filter=gsi_name eq " + "'" + "Unknown" + "'").Result;
                            if (retrieveResponseUnknown.IsSuccessStatusCode)
                            {
                                var parseResponseUnknown = JObject.Parse(retrieveResponseUnknown.Content.ReadAsStringAsync().Result);
                                dynamic deserializeResponseUnknown = JsonConvert.DeserializeObject(parseResponseUnknown.ToString());
                                count = deserializeResponseUnknown.value.Count;
                                log.LogInformation($"Count for ZipCode record without isPrimary value: {count}");
                                if (count != 0)
                                {
                                    fetchedGuid = deserializeResponseUnknown.value[0].gsi_zipcodeid.Value;
                                    log.LogInformation($"ZipCode: {fetchedGuid}");
                                }
                            }
                            else
                            {
                                log.LogInformation($"CRM Record Id Couldn't Retrieved For ZipCode:");
                            }
                        }
                    }
                    else
                    {
                        log.LogInformation($"CRM Record Id Couldn't Retrieved For ZipCode:");
                    }
                }
            }
            else
            {
                log.LogInformation($"CRM Record Id Couldn't Retrieved For ZipCode:");
            }
        }
        else if (selectEntityName == "leads")
        {
            selectNameField = "leadid";
            value = websiteValue;
            var retrieveResponse = client.GetAsync(query + "select=" + selectNameField + "&$filter=gsi_clientid eq '" + value + "'").Result;
            log.LogInformation($"Request:" + query + "select=" + selectNameField + "&$filter=gsi_clientid eq '" + value + "'");
            if (retrieveResponse.IsSuccessStatusCode)
            {
                var parseResponse = JObject.Parse(retrieveResponse.Content.ReadAsStringAsync().Result);
                dynamic deserializeResponse = JsonConvert.DeserializeObject(parseResponse.ToString());
                log.LogInformation($"Response: {parseResponse.ToString()}");
                count = deserializeResponse.value.Count;
                if (count != 0)
                {
                    fetchedGuid = deserializeResponse.value[0].leadid.Value;
                    log.LogInformation($"leadid: {fetchedGuid}");
                }
            }
            else
            {
                log.LogInformation($"CRM Record Id Couldn't Retrieved For Lead:");
            }
        }
    }
    catch (Exception exception)
    {
        log.LogInformation($"Exception: {exception.Message}");
        throw new Exception($"Something is wrong: {exception.Message}");
    }
    return fetchedGuid;
}
//Create Notes Against Newly Created Lead
private static async Task<Guid> CreateNotesInCE(Lead lead, AccessDetails accessDetails, string accessToken, ILogger log, string leadId)
{
    Guid noteId = Guid.Empty;
    string comments = string.Empty;
    comments = lead.Comments;
    if (comments != string.Empty)
    {
        JObject notesObj = new JObject { };
        notesObj["notetext"] = comments;
        notesObj.Add("objectid_lead@odata.bind", "/leads(" + leadId + ")");
        try
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(accessDetails.WebAPIURL);
                client.Timeout = new TimeSpan(0, 2, 0);
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Add("MSCRM.SuppressDuplicateDetection", "false");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                //set Create request and content 
                HttpRequestMessage createRequest = new HttpRequestMessage(HttpMethod.Post, "annotations")
                {
                    Content = new StringContent(notesObj.ToString(), Encoding.UTF8, "application/json")
                };
                HttpResponseMessage createResponse = await client.SendAsync(createRequest);
                //verify successful call 
                createResponse.EnsureSuccessStatusCode();
                //get result content 
                string noteURL = createResponse.Headers.GetValues("OData-EntityId").FirstOrDefault();
                //extract GUID from URL
                noteId = Guid.Parse(Regex.Match(noteURL, @"\(([^)]*)\)").Groups[1].Value);
            }
        }
        catch (Exception exception)
        {
            log.LogInformation($"Exception: {exception.Message}");
            throw new Exception($"Something is wrong: {exception.Message}");
        }
    }
    return noteId;
}
public static string RemoveEscapeChars(string originalString)
{
    if (originalString == null)
    {
        return null;
    }
    else
    {
        var modifiedValue = string.Empty;
        modifiedValue = originalString.Replace("\0", string.Empty);
        modifiedValue = Regex.Replace(modifiedValue, @"[^\u0000-\u007F]+", string.Empty);
        return modifiedValue;
    }
}
// Represents Lead Record Data
internal class Lead
{
    internal string ClientId { get; set; }
    internal string FirstName { get; set; }
    internal string LastName { get; set; }
    internal string Email { get; set; }
    internal int? EmailValidationStatus { get; set; }
    internal string Phone { get; set; }
    internal string PhoneType { get; set; }
    internal int? PhoneValidationStatus { get; set; }
    internal string Address { get; set; }
    internal string Zip { get; set; }
    internal string Comments { get; set; }
    internal bool UserEmailOptedIn { get; set; }
    internal bool UserPhoneOptedIn { get; set; }
    internal bool UserSMSOptedIn { get; set; }
    internal string IPAddress { get; set; }
    internal string GaCampaign { get; set; }
    internal string GaClientID { get; set; }
    internal string GaLandingPage { get; set; }
    internal string GaMedium { get; set; }
    internal string GaSource { get; set; }
    internal int Liquidity { get; set; }
    internal IList<int> LeadMarketsofInterest { get; set; }
    internal string OtherMarketsofInterest { get; set; }

    internal string StreetAddress1 { get; set; }
    internal string StreetAddress2 { get; set; }
    internal string City { get; set; }
    internal string State { get; set; }

    internal string PhoneValidationResponse { get; set; }
    internal string EmailValidationResponse { get; set; }


}

public class PhoneValidationResponse
{
    public string Confidence { get; set; }
    public string Number { get; set; }
    public string ValidatedPhoneNumber { get; set; }
    public string FormattedPhoneNumber { get; set; }
    public string PhoneType { get; set; }
    public string PortedDate { get; set; }
    public string DisposableNumber { get; set; }
}

public class EmailValidationResponse
{
    public string Confidence { get; set; }
    public string Email { get; set; }
    public string VerboseOutput { get; set; }
    public List<string> didYouMean { get; set; }
}

internal class AccessDetails
{
    internal string WebAPIURL { get; set; }
    internal string ApplicationId { get; set; }
    internal string Secret { get; set; }
    internal string Resource { get; set; }
}