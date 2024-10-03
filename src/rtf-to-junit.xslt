<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="2.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes"/>
  <xsl:param name="revit_version" select="2019"/>

  <xsl:template match="/test-run | /test-results">
    <testsuites name="{@name}"
	            tests="{count(test-suite/results/test-suite/results/test-case)}"
				failures="{count(test-suite/results/test-suite/results/test-case[@result='Failure'])}"
				errors="{count(test-suite/results/test-suite/results/test-case[@result='Error'])+count(test-suite/results/test-suite/results/test-case[@result='TimedOut'])}"
				skipped="{count(test-suite/results/test-suite/results/test-case[@result='NotRun'])+count(test-suite/results/test-suite/results/test-case[@result='Ignored'])+count(test-suite/results/test-suite/results/test-case[@result='Skipped'])}"
				assertions="{count(test-suite/results/test-suite/results/test-case[@result='Invalid'])+count(test-suite/results/test-suite/results/test-case[@result='Inconclusive'])}"
				time="{sum(test-suite/results/test-suite/results/test-case/@time)}"
				timestamp="{concat(@date,'T',@time)}">
		<xsl:apply-templates/>
    </testsuites>
  </xsl:template>

  <xsl:template match="test-suite">
	<xsl:apply-templates/>
  </xsl:template>
  
  <xsl:template match="results">
	<xsl:if test="test-case">
		<testsuite name="{concat(../@name,' - Revit ', $revit_version)}"
		           tests="{count(test-case)}"
				   failures="{count(test-case[@result='Failure'])}"
				   errors="{count(test-case[@result='Error'])+count(test-case[@result='TimedOut'])}"
				   skipped="{count(test-case[@result='NotRun'])+count(test-case[@result='Ignored'])+count(test-case[@result='Skipped'])}"
				   assertions="{count(test-case[@result='Invalid'])+count(test-case[@result='Inconclusive'])}"
				   time="{sum(test-case/@time)}">
			<xsl:apply-templates/>
		</testsuite>
	</xsl:if>
	<xsl:if test="not(test-case)">
		<xsl:apply-templates/>
	</xsl:if>
  </xsl:template>

  <xsl:template match="test-case">
    <testcase name="{concat(@name, ' - Revit ', $revit_version)}" time="{@time}" status="{@result}">
		<xsl:if test="@result = 'Skipped' or @result = 'Ignored'">
			<skipped/>
		</xsl:if>
		<xsl:apply-templates/>
    </testcase>
  </xsl:template>

  <xsl:template match="command-line"/>
  <xsl:template match="settings"/>
  <xsl:template match="filter"/>

  <xsl:template match="output">
    <system-out>
      <xsl:value-of select="."/>
    </system-out>
  </xsl:template>

  <xsl:template match="stack-trace">
  </xsl:template>

  <xsl:template match="test-case/failure">
    <failure message="{./message}">
      <xsl:value-of select="./stack-trace"/>
    </failure>
  </xsl:template>

  <xsl:template match="test-suite/failure"/>

  <xsl:template match="test-case/reason">
    <xsl:if test="./message != null">
      <skipped message="{./message}"/>
    </xsl:if>
  </xsl:template>
  
  <xsl:template match="test-case/assertions">
  </xsl:template>

  <xsl:template match="test-suite/reason"/>

  <xsl:template match="properties"/>
</xsl:stylesheet>
