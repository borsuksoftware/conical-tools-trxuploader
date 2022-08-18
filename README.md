This repository contains the source code for a .net command line tool for uploading trx files to Conical.

For more details on Conical, please see the main website - https://conical.cloud

## Purpose
The tool exists to make it easier to manage the CI workflow by providing a general purpose command line based tool to upload the results of unit tests to Conical without the need for custom apps. 

Although the primary purposes of Conical isn't to handle unit tests (our default expectation is that real unit tests are handled as pass-fail by the CI system), in a lot of organisations there are a lot of integration / regression tests which are packaged as unit tests for convenience etc. To that end, we want to make it easy to improve the visibility of those results.

## Usage
The tool works by taking a single trx file and uploading it to a Conical instance as a test run set where each unit test is mapped to an individual test run. These tests are grouped together by splitting their names (. => new layer in the hierarchy). For each test, the full details of the test run is included as the 'results xml' with any additional logging being copied through as 'logs'.

There's currently no support for any additional files (largely due to not having an example file) but this can be added as requested. Please raise an issue with the details / sample files.

All settings can be specified through the command line. It is also possible to specify an appsettings.json file with defaults if desired.

Full details of the supported options can be found by running the tool with no arguments or by passing in -help.

## FAQs
#### We've found a bug, what do we do?
Contact us / raise an issue / raise a PR with the fix.

#### Our use-case is slightly different, what should we do?
Short answer - Get in touch with us. 
Long answer - If your use-case can be added to the tool and be used by other people, then we'll see what we can do. If it's more specialised, then we'd recommend copying the existing project and make your changes under a different tool name.

#### I have suggestions for improvements, what do I do?
Get in touch with us either via our website or raise an issue in the project