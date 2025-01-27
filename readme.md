# Authentication Process

The authentication process was inspired by the authentication process in [southern-company-api](https://github.com/apearson/southern-company-api?tab=readme-ov-file#how-authentication-works) (by [apearson](https://github.com/apearson)) but differs slightly. After changes in the authentication process this library, [southern_company_api](https://github.com/Lash-L/southern_company_api/blob/main/src/southern_company_api/parser.py) by [Lash-L](https://github.com/Lash-L), was also helpful in disconvering the new workflow.

1. Get `data-aft` verification token. `HttpGet` from `https://webauth.southernco.com/account/login`. There's an html element with an id of `webauth-aft`, on that there is a data attribute of `data-aft`. The value of that is the verification token.

2. Get `ScWebToken`. `HttpPost` to `https://webauth.southernco.com/api/login` with a header of `RequestVerificationToken` with a value from step 1 and json body with the username/password credentials (plus some other stuff).
   The json response from that has a node at `Data.Html` which can be parsed as an html document. In there is an `input` element with a `ScWebToken` attribute. Get the value from that attribute. 

3. Get `SouthernJwtCookie`. `HttpPost` to `https://customerservice2.southerncompany.com/Account/LoginComplete?ReturnUrl=/billing/home`. The return url can be anything but it needs to be present and not `null` otherwise you'll get a 500 response.
   The response sets a cookie (`set-cookie` header) with a value of `SouthernJwtCookie`. Get the value of that cookie.

4. Get `ScJwtToken`. `HttpGet` to `https://customerservice2.southerncompany.com/Account/LoginValidated/JwtToken` with a cookie of `SouthernJwtCookie={the value from step 4}`.
   The response sets a cookie (`set-cookie` header) with a value of `ScJwtToken`. Get the value of that cookie.

5. Every subsequent request can add a header of `"Authorization": "Bearer " + ScJwtToken` to authorize the request.
