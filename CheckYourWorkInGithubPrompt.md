When I direct the agent to "Check your work in github":

1. **Pre-requisites for Analysis**: 
   - The user **MUST** first provide the specific **Version Number** (e.g., `v0.1.0`) being compiled.
   - The user **MUST** provide the relevant **Log text** (or a direct log URL / snippet) demonstrating the failure.
   - If either of these is missing, the agent must ask the user to provide them before proceeding with any analysis.

2. **Actions Directory check**: 
   - State the relevant URL of the build run (e.g., `https://github.com/JON99999/Interstitial-er/actions/runs/...`) if provided, or refer to `https://github.com/JON99999/Interstitial-er/actions` for context.
   - The agent is explicitly **allowed and encouraged** to use the web search tool to search GitHub issues, Microsoft documentation, or general developer forums for solutions to specific build errors or environment/SDK issues.
   - Do **NOT** attempt to scrape or parse remote run logs directly from GitHub Actions pages unless a direct, raw logs URL is explicitly provided by the user. Rely primarily on the log text or snippet provided by the user in the prompt.

3. **Identify & Summarize Issues**:
   - Review the provided log details and/or web-search findings specifically for the current release action.
   - Summarize the identified errors by row number or step name, providing a brief, literal, technical description of what they mean, alongside possible solution paths.

4. **Action List (Suggest Only)**:
   - SUGGEST, but **DO NOT** implement any code changes, workflow adjustments, or scripts modifications until the user explicitly approves and confirms the actions in a new prompt.

5. **Log & Change History Ledger (`/GitHubChecksHistory.md`)**:
   - Every time a "Check your work in github" request is made, log the entry in `/GitHubChecksHistory.md` under a new date and version header.
   - Record the version number, the log text or URL supplied, and the initial analysis/recommendations.
   - When the user subsequently approves changes based on this analysis, update the file to record the approved changes, files modified, and outcome of the fixes.

6. **Prompts Maintenance**:
   - Never change the text of this prompt instruction file unless explicitly told to do so.
