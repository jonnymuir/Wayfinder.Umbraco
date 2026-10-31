# Brief: Northgate Pensions member site

Build this site in Umbraco. Wayfinder for Umbraco is installed. Make the solution as simple as
possible, with minimal code. Any member or scheme data comes from our pensions administration
system, which has no API yet: think of the simplest way to mock it so we can swap in the real API
later, and write down what you assumed.

## Pages and features

1. **Home**: a hero with the strapline "Your pension, in plain sight", three cards linking to the
   services below, and a news strip.
2. **About the scheme**: two paragraphs and a table of contribution rates.
3. **Update your contact details**: a signed-in member sees their current address and email
   (from the administration system), changes either, checks their answers and confirms. The
   change is sent to the administration system.
4. **Ask a question**: anyone can send a question with their name, email and a message. The
   member services team picks questions up from a queue, replies, and can close them. The person
   who asked gets the reply by email.
5. **Retirement estimate**: a signed-in member enters the age they want to retire at (55 to 75).
   The site shows their current pot (from the administration system) and an estimated annual
   pension: pot × (1.04 ^ years to retirement) ÷ 22. Members over 75 are told to call instead.
6. **Look and feel**: navy `#14213D` primary, gold `#FCA311` accent, white backgrounds, a
   rounded sans-serif typeface.

Tell me about any part of this that does not suit the approach before you build it.
