# How to fetch utility data

Fill in the following objects in `secrets.json` (these stay on your computer and are not synced to GitHub):

```
{
   "GeorgiaPowerCredentials":
   {
      "username": "your Georgia power user name",
      "password": "your Georgia Power password"
   },
   "fetchRange":{
      "startDate": "MM/dd/YYYY",
      "endDate": "MM/dd/YYYY"
   }
}
```

# Authentication Process

The authentication process was inspired by the authentication process in [southern-company-api](https://github.com/apearson/southern-company-api?tab=readme-ov-file#how-authentication-works) (by [apearson](https://github.com/apearson)) but differs slightly. After changes in the authentication process this library, [southern_company_api](https://github.com/Lash-L/southern_company_api/blob/main/src/southern_company_api/parser.py) by [Lash-L](https://github.com/Lash-L), was also helpful in disconvering the new workflow.

1. Start with a `GET` to the customer portal login page with `appId=pr-occ-forgerock`. The portal redirects through WebAuth to ForgeRock's OAuth authorize URL. The authorize URL contains the login state and PKCE challenge needed to finish the sign-in.

2. Send a `POST` to `https://customerlogin.southernco.com/am/json/realms/root/realms/alpha/authenticate` with `authIndexType=service`, `authIndexValue=occSecureLogin`, and the authorize URL in `goto`. The first request starts the ForgeRock login journey and returns an `authId` and callbacks.

3. Send the callbacks back to the same endpoint with the `authId`. Fill the `NameCallback` with the username, the `PasswordCallback` with the password, and choose `Log In` in the confirmation callback. Keep the callback fields and journey metadata from the response. A successful response contains a ForgeRock `tokenId`.

4. Get the ForgeRock SSO cookie name from `https://customerlogin.southernco.com/am/json/realms/root/realms/alpha/serverinfo/*`. Send the `tokenId` as a cookie with that name when continuing the authorize request.

5. Request the saved authorize URL and follow its redirects. When ForgeRock returns a form containing the authorization `code` and `state`, post that form to its WebAuth callback and continue following redirects to the customer portal.

6. Get the portal JWT from `https://customerservice2.southerncompany.com/Account/LoginValidated/JwtToken`. The current portal can return it in a response header such as `ScJwtToken` or `ScSoftAuthJwtToken`; older responses may set it as a cookie. Use the JWT as a bearer token on later requests.

Account and usage data now come from the OCC APIs. The account list is `https://occaccountapi.southerncompany.com/api/v1/Cap/`, and an account summary is at `/api/v1/Accounts/{accountNumber}/Summary`. Daily and hourly usage use `https://occmypowerusageapi.southerncompany.com/api/v1/MyPowerUsage/UsageGraphData/{serviceAgreementId}/{Daily|Hourly}` with the account and service IDs from the summary.
