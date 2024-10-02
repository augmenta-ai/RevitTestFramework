<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="2.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes"/>

  <xsl:template match="/test-run | /test-results">
    <testsuites>
		<xsl:attribute name="name">
			<xsl:value-of select="@name"/>
		</xsl:attribute>
		<xsl:attribute name="tests">
			<xsl:value-of select="count(test-suite/results/test-suite/results/test-case)"/>
		</xsl:attribute>
		<xsl:attribute name="failures">
			<xsl:value-of select="count(test-suite/results/test-suite/results/test-case[@result='Failure'])"/>
		</xsl:attribute>
		<xsl:attribute name="errors">
			<xsl:value-of select="count(test-suite/results/test-suite/results/test-case[@result='Error'])+count(test-suite/results/test-suite/results/test-case[@result='TimedOut'])"/>
		</xsl:attribute>
		<xsl:attribute name="skipped">
			<xsl:value-of select="count(test-suite/results/test-suite/results/test-case[@result='NotRun'])+count(test-suite/results/test-suite/results/test-case[@result='Ignored'])+count(test-suite/results/test-suite/results/test-case[@result='Skipped'])"/>
		</xsl:attribute>
		<xsl:attribute name="assertions">
			<xsl:value-of select="count(test-suite/results/test-suite/results/test-case[@result='Invalid'])+count(test-suite/results/test-suite/results/test-case[@result='Inconclusive'])"/>
		</xsl:attribute>
		<xsl:attribute name="time">
			<xsl:value-of select="sum(test-suite/results/test-suite/results/test-case/@time)"/>
		</xsl:attribute>
		<xsl:attribute name="timestamp">
			<xsl:value-of select="concat(@date,'T',@time)"/>
		</xsl:attribute>
		<xsl:apply-templates/>
    </testsuites>
  </xsl:template>

  <xsl:template match="test-suite">
	<xsl:apply-templates/>
  </xsl:template>
  
  <xsl:template match="results">
	<xsl:if test="test-case">
		<testsuite>
			<xsl:attribute name="name">
				<xsl:value-of select="concat(../@name,' - ', ../@description)"/>
			</xsl:attribute>
			<xsl:attribute name="tests">
				<xsl:value-of select="count(test-case)"/>
			</xsl:attribute>
			<xsl:attribute name="failures">
				<xsl:value-of select="count(test-case[@result='Failure'])"/>
			</xsl:attribute>
			<xsl:attribute name="errors">
				<xsl:value-of select="count(test-case[@result='Error'])+count(test-case[@result='TimedOut'])"/>
			</xsl:attribute>
			<xsl:attribute name="skipped">
				<xsl:value-of select="count(test-case[@result='NotRun'])+count(test-case[@result='Ignored'])+count(test-case[@result='Skipped'])"/>
			</xsl:attribute>
			<xsl:attribute name="assertions">
				<xsl:value-of select="count(test-case[@result='Invalid'])+count(test-case[@result='Inconclusive'])"/>
			</xsl:attribute>
			<xsl:attribute name="time">
				<xsl:value-of select="sum(test-case/@time)"/>
			</xsl:attribute>
			<xsl:apply-templates/>
		</testsuite>
	</xsl:if>
	<xsl:if test="not(test-case)">
		<xsl:apply-templates/>
	</xsl:if>
  </xsl:template>

  <xsl:template match="test-case">
    <testcase name="{@name}" time="{@time}" status="{@result}">
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
