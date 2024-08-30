# Authentication Process

The authentication process was inspired by the authentication process in [southern-company-api](https://github.com/apearson/southern-company-api?tab=readme-ov-file#how-authentication-works) but differs slightly.

1. Get `data-aft` verification token. `HttpGet` from `https://webauth.southernco.com/account/login`. There's an html element with an id of `webauth-aft`, on that there is a data attribute of `data-aft`. The value of that is the verification token.

2. Get `ScWebToken`. `HttpPost` to `https://webauth.southernco.com/api/login` with a header of `RequestVerificationToken` with a value from step 1 and json body with the username/password credentials (plus some other stuff).
   The json response from that has a node at `Data.Html` which can be parsed as an html document. In there is an `input` element with a `ScWebToken` attribute. Get the value from that attribute. 

3. Get `ScJwtToken`. `HttpPost` to `https://customerservice2.southerncompany.com/Account/LoginComplete?ReturnUrl=/billing/home`. The return url can be anything but it needs to be present and not `null` otherwise you'll get a 500 response.
   The response sets a cookie (`set-cookie` header) with a value of `ScJwtToken`. Get the value of that cookie.

4. Every subsequent request can add a header of `"Authorization": "Bearer " + Jwt` to authorize the request.
